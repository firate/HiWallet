# Ön API, public: işyerinin sistem entegrasyonu. Repo KÖKÜNDEN build edilir;
# Directory.Build.props ve Directory.Packages.props lazım (merkezi versiyonlama).
#   docker build -f src/business/BusinessApi/Dockerfile .
#
# WalletService.Core KOPYALANMIYOR: bu host ona referans vermiyor.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kaynak değişse de restore katmanı cache'ten gelsin.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/business/BusinessApi/BusinessApi.csproj src/business/BusinessApi/
COPY src/shared/EdgeApi.Core/EdgeApi.Core.csproj src/shared/EdgeApi.Core/
COPY src/shared/Shared.Contracts/Shared.Contracts.csproj src/shared/Shared.Contracts/
COPY src/shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/shared/Shared.Infrastructure/
RUN dotnet restore src/business/BusinessApi/BusinessApi.csproj

COPY src/ src/
RUN dotnet publish src/business/BusinessApi/BusinessApi.csproj \
        -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Root olarak çalışmıyor. aspnet imajı 'app' kullanıcısını hazır getiriyor.
USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "HiWallet.BusinessApi.dll"]
