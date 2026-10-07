# Ingress'i yok: dışarıdan kimse bağlanmıyor, mesajları kendisi kuyruktan çekiyor.
# Açtığı tek port sağlık ucu için.
#
# Repo KÖKÜNDEN build edilir:
#   docker build -f src/core/wallet/WalletConsumer/Dockerfile .
#
# Migration bundle'ı BURADA DEĞİL: şema WalletService.Core'un.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/core/wallet/WalletConsumer/WalletConsumer.csproj src/core/wallet/WalletConsumer/
COPY src/core/wallet/WalletService.Core/WalletService.Core.csproj src/core/wallet/WalletService.Core/
COPY src/shared/Shared.Contracts/Shared.Contracts.csproj src/shared/Shared.Contracts/
COPY src/shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/shared/Shared.Infrastructure/
RUN dotnet restore src/core/wallet/WalletConsumer/WalletConsumer.csproj

COPY src/ src/
RUN dotnet publish src/core/wallet/WalletConsumer/WalletConsumer.csproj \
        -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "HiWallet.WalletConsumer.dll"]
