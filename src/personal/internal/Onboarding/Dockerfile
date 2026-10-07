# Repo KÖKÜNDEN build edilir; Directory.Build.props ve Directory.Packages.props
# lazım (merkezi versiyonlama). compose'da context: . olarak veriliyor.
#   docker build -f src/personal/internal/Onboarding/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kaynak değişse de restore katmanı cache'ten gelsin.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/personal/internal/Onboarding/Onboarding.csproj src/personal/internal/Onboarding/
COPY src/shared/Shared.Contracts/Shared.Contracts.csproj src/shared/Shared.Contracts/
COPY src/shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/shared/Shared.Infrastructure/
RUN dotnet restore src/personal/internal/Onboarding/Onboarding.csproj

COPY src/ src/
RUN dotnet publish src/personal/internal/Onboarding/Onboarding.csproj \
        -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Migration bundle. Şemanın sahibi onboarding_owner olarak koşuyor; uygulama
# onboarding_app ile bağlanıyor ve consents üzerindeki REVOKE ona işliyor.
# ---------------------------------------------------------------------------
FROM build AS migrator-build
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
        --project src/personal/internal/Onboarding \
        --startup-project src/personal/internal/Onboarding \
        --self-contained -r linux-musl-x64 \
        -o /app/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS migrator
WORKDIR /app
COPY --from=migrator-build /app/efbundle .
ENTRYPOINT ["./efbundle"]

# ---------------------------------------------------------------------------
# Uygulama
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "HiWallet.Onboarding.dll"]
