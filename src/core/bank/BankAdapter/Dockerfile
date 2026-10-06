# Repo KÖKÜNDEN build edilir; Directory.Build.props ve Directory.Packages.props
# lazım (merkezi versiyonlama). compose'da context: . olarak veriliyor.
#   docker build -f src/BankAdapter/Dockerfile .
#
# Bu servis BİZİM ve üretimde de koşuyor. Sahte olan taraf Bank.Fake; buradaki
# sahtelik yalnızca Bank__BaseUrl değerinde.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kaynak değişse de restore katmanı cache'ten gelsin.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/BankAdapter/BankAdapter.csproj src/BankAdapter/
COPY src/BankIntegration.Core/BankIntegration.Core.csproj src/BankIntegration.Core/
COPY src/Shared/Shared.Contracts/Shared.Contracts.csproj src/Shared/Shared.Contracts/
COPY src/Shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/Shared/Shared.Infrastructure/
RUN dotnet restore src/BankAdapter/BankAdapter.csproj

COPY src/ src/
RUN dotnet publish src/BankAdapter/BankAdapter.csproj \
        -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Migration bundle. Şemanın sahibi BankIntegration.Core: iki host paylaşıyor ve
# migration'ı yalnızca bu bundle koşturuyor. Hiçbir uygulama startup'ta migrate
# etmiyor (baseline.md).
# ---------------------------------------------------------------------------
FROM build AS migrator-build
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
        --project src/BankIntegration.Core \
        --startup-project src/BankIntegration.Core \
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

ENTRYPOINT ["dotnet", "HiWallet.BankAdapter.dll"]
