# Ön API, public: bireysel müşterinin web uygulamasının BFF'i. Repo KÖKÜNDEN build
# edilir; Directory.Build.props ve Directory.Packages.props lazım (merkezi versiyonlama).
#   docker build -f src/personal/edge/PersonalWebBff/Dockerfile .
#
# Uygulama (yanındaki PersonalWeb) ayrı aşamada derleniyor ve BFF'in wwwroot'una
# kopyalanıyor: sayfalar ve API aynı adresten, cookie tek kökende.
#
# WalletService.Core KOPYALANMIYOR: bu host ona referans vermiyor.

FROM node:24-alpine AS web
WORKDIR /web

# Önce yalnızca paket listesi: kaynak değişse de npm ci katmanı cache'ten gelsin.
COPY src/personal/edge/PersonalWeb/package.json src/personal/edge/PersonalWeb/package-lock.json ./
RUN npm ci --no-fund --no-audit

COPY src/personal/edge/PersonalWeb/index.html src/personal/edge/PersonalWeb/tsconfig.json src/personal/edge/PersonalWeb/vite.config.ts ./
COPY src/personal/edge/PersonalWeb/src ./src
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/personal/edge/PersonalWebBff/PersonalWebBff.csproj src/personal/edge/PersonalWebBff/
COPY src/shared/EdgeApi.Core/EdgeApi.Core.csproj src/shared/EdgeApi.Core/
COPY src/shared/Shared.Contracts/Shared.Contracts.csproj src/shared/Shared.Contracts/
COPY src/shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/shared/Shared.Infrastructure/
RUN dotnet restore src/personal/edge/PersonalWebBff/PersonalWebBff.csproj

COPY src/ src/
RUN dotnet publish src/personal/edge/PersonalWebBff/PersonalWebBff.csproj \
        -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Cookie'yi şifreleyen anahtarların dizini. Volume ilk bağlandığında sahipliği
# buradan alıyor; uygulama root olmadan yazabiliyor.
RUN mkdir -p /app/keys && chown app:app /app/keys

# Root olarak çalışmıyor. aspnet imajı 'app' kullanıcısını hazır getiriyor.
USER app

COPY --from=build /app/publish .
COPY --from=web /web/dist ./wwwroot

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "HiWallet.PersonalWebBff.dll"]
