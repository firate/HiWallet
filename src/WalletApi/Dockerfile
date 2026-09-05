# Public ingress. Repo KÖKÜNDEN build edilir; Directory.Build.props ve
# Directory.Packages.props lazım (merkezi versiyonlama).
#   docker build -f src/WalletApi/Dockerfile .
#
# Migration bundle'ı BURADA DEĞİL: şema WalletService.Core'un ve migrator onun
# Dockerfile'ından geliyor.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kaynak değişse de restore katmanı cache'ten gelsin.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/WalletApi/WalletApi.csproj src/WalletApi/
COPY src/WalletService.Core/WalletService.Core.csproj src/WalletService.Core/
COPY src/Shared/Shared.Contracts/Shared.Contracts.csproj src/Shared/Shared.Contracts/
COPY src/Shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/Shared/Shared.Infrastructure/
RUN dotnet restore src/WalletApi/WalletApi.csproj

COPY src/ src/
RUN dotnet publish src/WalletApi/WalletApi.csproj \
        -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Root olarak çalışmıyor. aspnet imajı 'app' kullanıcısını hazır getiriyor.
USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "HiWallet.WalletApi.dll"]
