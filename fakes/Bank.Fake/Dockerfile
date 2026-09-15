# Repo KÖKÜNDEN build edilir; Directory.Build.props ve Directory.Packages.props
# lazım (merkezi versiyonlama). compose'da context: . olarak veriliyor.
#   docker build -f fakes/Bank.Fake/Dockerfile .
#
# Bu servis BANKANIN YERİNDE DURUYOR ve üretimde çalışmaz — gerçek banka
# entegrasyonu geldiğinde bu klasör tamamen siliniyor. Yine de diğerleriyle aynı
# şekilde paketleniyor: compose'da gerçek bir dış bağımlılık gibi durması, zincirin
# gerçekten dağıtık koştuğunu gösteriyor.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kaynak değişse de restore katmanı cache'ten gelsin.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY fakes/Bank.Fake/Bank.Fake.csproj fakes/Bank.Fake/
COPY src/Shared/Shared.Contracts/Shared.Contracts.csproj src/Shared/Shared.Contracts/
COPY src/Shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/Shared/Shared.Infrastructure/
RUN dotnet restore fakes/Bank.Fake/Bank.Fake.csproj

COPY src/ src/
COPY fakes/ fakes/
RUN dotnet publish fakes/Bank.Fake/Bank.Fake.csproj \
        -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Migration bundle. Hiçbir uygulama startup'ta migrate etmiyor; şemayı ayrı bir
# job kuruyor (baseline.md).
# ---------------------------------------------------------------------------
FROM build AS migrator-build
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
        --project fakes/Bank.Fake \
        --startup-project fakes/Bank.Fake \
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

ENTRYPOINT ["dotnet", "HiWallet.Bank.Fake.dll"]
