# Wallet şemasını uygulayan migration bundle'ı.
#
# Neden burada, host'ların birinde değil: şema Core'un. wallet-api ve topup-consumer
# ikisi de aynı tabloları kullanıyor, ikisi de sahibi değil. Migrator'ı birinin
# Dockerfile'ına koymak "şema o host'un" izlenimi verirdi.
#
# Ayrı bir servis olarak koşuyor çünkü migration wallet_owner ile, uygulamalar
# wallet_app ile bağlanıyor (decisions.md madde 5). Uygulamanın kendi başlangıcında
# migrate etmesi hem bu ayrımı bozardı hem de çok instance'ta aynı anda migrate
# denemesi olurdu.
#
# Repo KÖKÜNDEN build edilir:
#   docker build -f src/WalletService.Core/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/WalletService.Core/WalletService.Core.csproj src/WalletService.Core/
COPY src/Shared/Shared.Contracts/Shared.Contracts.csproj src/Shared/Shared.Contracts/
COPY src/Shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/Shared/Shared.Infrastructure/
RUN dotnet restore src/WalletService.Core/WalletService.Core.csproj

COPY src/ src/

RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="$PATH:/root/.dotnet/tools"

# Core kendi startup project'i olabiliyor: tasarım zamanı fabrikası (WalletDbContextFactory)
# onun içinde. Host'lardan birini startup vermek gereksiz yere API'yi de derletirdi.
RUN dotnet ef migrations bundle \
        --project src/WalletService.Core \
        --startup-project src/WalletService.Core \
        --self-contained -r linux-musl-x64 \
        -o /app/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS migrator
WORKDIR /app
COPY --from=build /app/efbundle .
ENTRYPOINT ["./efbundle"]
