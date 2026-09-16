# Forgot password

**Status:** approved, 2026-09-16

## The problem

An employee who forgets their password has no way back in. Today the only route is an
administrator opening Administration → Users, pressing **Reset Password**, and passing the
temporary password to them by hand. That costs an admin's time, and out of hours it costs the
employee their morning.

This adds the ordinary self-service route: ask for a link by email, follow it, set a new password.

## What we are building

1. A **Forgot password?** link on the login page.
2. `/forgot-password` — enter an email, get a deliberately uninformative confirmation.
3. An email with a link that works once, for an hour.
4. `/reset-password` — set a new password, then sign in with it.
5. An **Email** settings page under Administration, so the SMTP account can be changed without a
   deploy, with a **Send test email** button.

## Decisions

### Mail settings live in PeopleCore's own table

The production database already holds SMTP credentials in `SystemSetting` and `settings`, but
those tables belong to another application that shares the database — they are PascalCase and
carry a `TenantId`, neither of which is PeopleCore's. Reading them would wed the two applications
to one schema, where a rename over there silently breaks password resets here.

PeopleCore gets its own single-row `email_settings` table, seeded by hand with the same values.

### Data Protection keys move into the database

Identity's password reset tokens are Data Protection payloads. By default a container generates a
fresh key ring at startup, so every deploy or restart would void every outstanding link — and, if
the SMTP password were encrypted the same way, make it unreadable.

`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` persists the key ring to a table. With a
fixed application name, keys survive restarts and are shared if the API is ever scaled out. The
same key ring encrypts the stored SMTP password.

This is the one piece of infrastructure the feature adds, and it pays off beyond it: anything else
needing encryption at rest can now use `IDataProtector`.

### Identity's own tokens, not a table of our own

`UserManager.GeneratePasswordResetTokenAsync` and `ResetPasswordAsync` already give us what the
rules require:

- the token carries its expiry, set to **one hour** through `DataProtectionTokenProviderOptions`;
- it embeds the security stamp, so completing a reset invalidates the link **and** every other
  session, the same mechanism role changes already rely on;
- it is bound to one user, and tampering fails the payload's own integrity check.

A bespoke `password_reset_tokens` table would add an audit trail, at the cost of hand-written
security code. Requests and completions are logged instead; if an audit trail is wanted later, it
can be added without changing the flow.

### The reset page reuses the change-password form

`ChangePasswordForm` already holds the new/confirm fields, the client-side policy checks (at least
8 characters, one digit, different from the old one) and the API error handling. It gains a mode
where the **current password** field is replaced by the token from the link. The rules stay in one
place, so the reset page cannot drift from My Profile.

### Silence about who has an account

Every outcome of `forgot-password` — unknown address, deactivated account, rate limit reached, SMTP
refusing the message — returns the same answer:

> If that address has an account, we've sent a link to reset the password.

Each case is logged with its reason, so an administrator can tell from the logs what happened
without the login page telling a stranger which addresses are real.

## The flow

```
Login page                    API                                Mailbox
  "Forgot password?"
      ↓
/forgot-password
  email ───────────► POST api/auth/forgot-password
                       ├─ no such account, deactivated, or
                       │  rate limited → log, send nothing
                       └─ otherwise → token, link ──────────────► "Reset your password"
      ◄──────────────  200, same message either way                     │
                                                                        ↓
/reset-password?email=…&token=…  ◄──────────────────────────────────────┘
  new + confirm ───► POST api/auth/reset-password
                       ├─ bad or expired token → 400
                       └─ ok → password set, stamp replaced,
                               lockout cleared, must-change cleared
      ◄──────────────  200
      ↓
/login  "Your password has been changed."
```

## Components

### API

`AuthController`, both `[AllowAnonymous]`:

- `POST api/auth/forgot-password` — `{ email }` → always `200` with the generic message.
- `POST api/auth/reset-password` — `{ email, token, newPassword }` → `200`, or `400` with
  "This link has expired or has already been used." for a bad, used or expired token, or the
  password policy's own message when the new password is refused.
- `GET api/auth/password-reset-available` — `{ available: bool }`, so the login page can hide the
  link when no SMTP account is configured. It reveals only whether mail is set up.

`EmailSettingsController` (`api/email-settings`), behind the new `settings.manage` permission:

- `GET` — the settings, with the password replaced by whether one is stored, never its value.
- `PUT` — save; an empty password field leaves the stored one untouched.
- `POST test` — send a test message to the caller's own address and return the real SMTP error on
  failure, since this is the person fixing it.

### Infrastructure

- `IEmailSender` / `MailKitEmailSender` — one `SendAsync(to, subject, html, text)`.
- `IEmailSettingsStore` — reads and writes the single `email_settings` row, encrypting and
  decrypting the password with `IDataProtector`.
- `PasswordResetMail` — builds the subject and body from the link and the sender's name.

### Web

- Login page: a **Forgot password?** link under the password field, shown only when
  `password-reset-available` says mail is configured.
- `/forgot-password`: one email field, the generic confirmation, and a way back to sign in.
- `/reset-password`: reads `email` and `token` from the query string, renders `ChangePasswordForm`
  in reset mode, and on success sends the user to `/login` with a success note. After the page
  loads it replaces the history entry so the token does not linger in the address bar.
- Administration → **Email**: the settings form and the test button.

## Permissions

The catalogue gains a nineteenth key, `settings.manage`: "Manage system settings — the email
account the app sends from." Admin holds it by rule, as it holds every permission; it is granted to
nobody else by default, and can be put on a custom role like any other. Mail configuration is
therefore delegable without handing over user or role management.

The Administration section of both menus gains an **Email** entry, shown only to holders of that
permission, and the section itself becomes visible to anyone holding `users.manage`,
`roles.manage` or `settings.manage`.

## Data

`email_settings`, a single row:

| Column | Type | Notes |
|---|---|---|
| `id` | int | always 1; a check constraint keeps it single-row |
| `host` | text | |
| `port` | int | defaults to 587 |
| `use_start_tls` | bool | defaults to true; many hosts block port 25 |
| `username` | text | nullable, for servers that don't authenticate |
| `password_protected` | text | nullable, encrypted with `IDataProtector` |
| `from_address` | text | |
| `from_name` | text | defaults to "PeopleCore" |
| `app_base_url` | text | the address reset links point at |
| `updated_at` | timestamptz | |

`data_protection_keys` as the EF Core package defines it.

## Rules

- **Expiry:** one hour, single use. Completing a reset replaces the security stamp, which both
  voids the link and signs the account out everywhere.
- **Lockout:** a successful reset clears a lockout from failed guesses, matching what an
  administrator's reset already does.
- **Must-change flag:** cleared, since the user has chosen this password themselves.
- **Deactivated accounts:** no mail, no hint.
- **Rate limits:** three requests per address and ten per IP an hour, held in memory. The app runs
  as a single container; a restart resetting the counters costs nothing here.
- **Unconfigured mail:** the endpoint answers normally and logs that nothing was sent; the login
  page hides the link; the settings page warns until a test succeeds.

## Testing

**Application tests** cover every branch of both endpoints with a fake sender: unknown address,
deactivated account, rate limit reached, the happy path (asserting the link carries the token and
the configured base URL), a tampered token, an expired token, and a successful reset clearing both
the lockout and the must-change flag. Also that `forgot-password` answers identically in all the
silent cases — the test that would catch someone "helpfully" adding a specific error later.

**Infrastructure tests** on the real Postgres fixture cover the settings round-trip, including that
the stored password is not readable as plain text and that saving without a password keeps the old
one; and — the test that justifies the key ring work — that a token issued by one provider instance
still validates in a new one built over the same database, which is what a restart does.

**Web tests** cover the two new pages, the login link appearing only when mail is configured, and
the reset page's expired-link message.

**By hand, after deploy:** the **Send test email** button, then one real reset end to end.

## Out of scope

- Changing the address an account signs in with.
- Any other notification email. This adds the sending machinery; queues, retries and templates can
  come when there is a second email to send.
- An audit trail of reset requests beyond the log.
- Two-factor authentication.
