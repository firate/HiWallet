# Ön API, public: bireysel müşterinin mobil uygulaması. Repo KÖKÜNDEN build edilir;
# Directory.Build.props ve Directory.Packages.props lazım (merkezi versiyonlama).
#   docker build -f src/PersonalMobileApi/Dockerfile .
#
# WalletService.Core KOPYALANMIYOR: bu host ona referans vermiyor.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kaynak değişse de restore katmanı cache'ten gelsin.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/PersonalMobileApi/PersonalMobileApi.csproj src/PersonalMobileApi/
COPY src/Shared/Shared.Contracts/Shared.Contracts.csproj src/Shared/Shared.Contracts/
COPY src/Shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/Shared/Shared.Infrastructure/
RUN dotnet restore src/PersonalMobileApi/PersonalMobileApi.csproj

COPY src/ src/
RUN dotnet publish src/PersonalMobileApi/PersonalMobileApi.csproj \
        -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Root olarak çalışmıyor. aspnet imajı 'app' kullanıcısını hazır getiriyor.
USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "HiWallet.PersonalMobileApi.dll"]
