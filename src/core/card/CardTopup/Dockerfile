# Repo KÖKÜNDEN build edilir; Directory.Build.props ve Directory.Packages.props
# lazım (merkezi versiyonlama). compose'da context: . olarak veriliyor.
#   docker build -f src/core/card/CardTopup/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kaynak değişse de restore katmanı cache'ten gelsin.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/core/card/CardTopup/CardTopup.csproj src/core/card/CardTopup/
COPY src/shared/Shared.Contracts/Shared.Contracts.csproj src/shared/Shared.Contracts/
COPY src/shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/shared/Shared.Infrastructure/
RUN dotnet restore src/core/card/CardTopup/CardTopup.csproj

COPY src/ src/
RUN dotnet publish src/core/card/CardTopup/CardTopup.csproj \
        -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Migration bundle. Tek rol: append-only zorlanacak tablo yok, yükleme satırı her
# geçişte güncelleniyor. Yine de AYRI servis olarak koşuyor — uygulamanın kendi
# başlangıcında migrate etmesi çok instance'ta aynı anda migrate denemesi olurdu.
# ---------------------------------------------------------------------------
FROM build AS migrator-build
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
        --project src/core/card/CardTopup \
        --startup-project src/core/card/CardTopup \
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

ENTRYPOINT ["dotnet", "HiWallet.CardTopup.dll"]
