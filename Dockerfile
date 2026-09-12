# PeopleCore
# Multi-stage build for the API and the Blazor WebAssembly client.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# PeopleCore.Web.csproj's BuildTailwindCss target runs `npm ci` and `npm run build:css` from
# inside `dotnet publish`, so Node has to be on PATH before the publish below - not after it.
# (SPMS.Training's Dockerfile has no Node, which is why this one is not a copy of it.)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
    && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# Directory.Build.props first: it sets M2NetCoreProject, which the ProjectReference conditions
# in PeopleCore.Application and PeopleCore.Domain read at restore time.
COPY Directory.Build.props PeopleCore.slnx ./

# Each .csproj by its own path - Docker COPY has no ** glob. All nine are needed because the
# restore below targets the whole solution.
COPY src/M2NET.Core/M2NET.Core.csproj src/M2NET.Core/
COPY src/PeopleCore.Domain/PeopleCore.Domain.csproj src/PeopleCore.Domain/
COPY src/PeopleCore.Application/PeopleCore.Application.csproj src/PeopleCore.Application/
COPY src/PeopleCore.Infrastructure/PeopleCore.Infrastructure.csproj src/PeopleCore.Infrastructure/
COPY src/PeopleCore.Reports/PeopleCore.Reports.csproj src/PeopleCore.Reports/
COPY src/PeopleCore.API/PeopleCore.API.csproj src/PeopleCore.API/
COPY src/PeopleCore.Web/PeopleCore.Web.csproj src/PeopleCore.Web/
COPY tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj tests/PeopleCore.Application.Tests/
COPY tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj tests/PeopleCore.Infrastructure.Tests/

RUN dotnet restore PeopleCore.slnx

# npm install as its own layer, keyed on the lockfile, so an ordinary source change rebuilds the
# stylesheet without reinstalling Tailwind. The csproj skips its own `npm ci` when node_modules
# already exists - which, after this line, it does.
COPY src/PeopleCore.Web/package.json src/PeopleCore.Web/package-lock.json src/PeopleCore.Web/
RUN npm ci --prefix src/PeopleCore.Web --no-audit --no-fund

COPY src/ src/

RUN dotnet publish src/PeopleCore.API/PeopleCore.API.csproj -c Release -o /app/api --no-restore
RUN dotnet publish src/PeopleCore.Web/PeopleCore.Web.csproj -c Release -o /app/web --no-restore


# ─── API ──────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app

# curl is for the HEALTHCHECK. The font packages are for PeopleCore.Reports: QuestPDF and
# PDFsharp render payslips and BIR 2316, and nothing in the code calls
# FontManager.RegisterFont, so glyphs resolve from system fonts. Without these the PDFs come
# out blank or substituted rather than failing, which is why a payslip is the first thing to
# check after a deploy.
#
# No LibreOffice. SPMS.Training installs libreoffice-writer and -draw to rasterise certificate
# templates; nothing in PeopleCore invokes soffice, and the two packages cost ~700 MB.
RUN apt-get update && apt-get install -y --no-install-recommends \
        curl \
        fontconfig \
        fonts-liberation \
        fonts-dejavu-core \
        libicu-dev \
        locales \
    && locale-gen en_US.UTF-8 \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LANG=en_US.UTF-8 \
    LC_ALL=en_US.UTF-8 \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/api .

EXPOSE 8080

# A generous start period: the container applies 11 migrations against Neon at boot, and Neon
# scales compute to zero, so the first request can wait on a cold start.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "PeopleCore.API.dll"]


# ─── Web ──────────────────────────────────────────────────────────────────────
FROM nginx:alpine AS web
WORKDIR /usr/share/nginx/html

COPY --from=build /app/web/wwwroot .

# .NET 10 fingerprints the framework JS filename (blazor.webassembly.<hash>.js), but the
# <script src> in index.html resolves through a placeholder, and import maps do not apply to
# classic script tags. Point the plain name at whichever fingerprinted file the publish wrote.
RUN cd _framework \
    && for f in blazor.webassembly.*.js; do \
         [ "$f" = "blazor.webassembly.js" ] && continue; \
         ln -sf "$f" blazor.webassembly.js; \
         break; \
       done

COPY nginx.conf /etc/nginx/nginx.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
