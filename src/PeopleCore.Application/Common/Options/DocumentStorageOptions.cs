namespace PeopleCore.Application.Common.Options;

/// <summary>
/// Names of the object-storage buckets the application writes to. These are deployment
/// configuration — a self-hosted MinIO install and a Cloudflare R2 account will not agree on
/// bucket names — so they are resolved from the active <c>Storage:Provider</c> section at
/// startup rather than hardcoded in the services that upload.
/// </summary>
/// <remarks>
/// Employee documents and applicant resumes deliberately keep <em>separate</em>, separately
/// configurable names: they already live in different buckets, and collapsing them onto one
/// name would orphan every file existing deployments have already uploaded. For the same
/// reason the defaults below must not change — they are the historical names, and they are
/// pinned by tests.
/// </remarks>
public class DocumentStorageOptions
{
    /// <summary>Historical bucket for employee documents.</summary>
    public const string DefaultBucketName = "peoplecore-documents";

    /// <summary>Historical bucket for applicant resumes.</summary>
    public const string DefaultResumesBucketName = "resumes";

    /// <summary>Bucket holding employee documents (contracts, government IDs, and the like).</summary>
    public string BucketName { get; set; } = DefaultBucketName;

    /// <summary>Bucket holding resumes uploaded through the public careers portal.</summary>
    public string ResumesBucketName { get; set; } = DefaultResumesBucketName;
}
