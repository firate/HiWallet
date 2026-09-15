# Repo KÖKÜNDEN build edilir; Directory.Build.props ve Directory.Packages.props
# lazım (merkezi versiyonlama). compose'da context: . olarak veriliyor.
#   docker build -f src/BankWebhook/Dockerfile .
#
# Bu servis BİZİM ve üretimde de koşuyor. Bankanın bizi çağırdığı uç; ayrı
# deployable olmasının sebebi ingress'inin olması (decisions.md madde 28, 35).
#
# MIGRATOR AŞAMASI YOK. hiwallet_bank şemasının sahibi BankIntegration.Core ve
# bundle'ı bank-adapter'ın Dockerfile'ı üretiyor. İki host aynı şema için ayrı
# bundle üretseydi hangisinin önce koştuğu şemayı belirlerdi.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kaynak değişse de restore katmanı cache'ten gelsin.
COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/BankWebhook/BankWebhook.csproj src/BankWebhook/
COPY src/BankIntegration.Core/BankIntegration.Core.csproj src/BankIntegration.Core/
COPY src/Shared/Shared.Contracts/Shared.Contracts.csproj src/Shared/Shared.Contracts/
COPY src/Shared/Shared.Infrastructure/Shared.Infrastructure.csproj src/Shared/Shared.Infrastructure/
RUN dotnet restore src/BankWebhook/BankWebhook.csproj

COPY src/ src/
RUN dotnet publish src/BankWebhook/BankWebhook.csproj \
        -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

USER app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "HiWallet.BankWebhook.dll"]
