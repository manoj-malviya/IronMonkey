FROM mcr.microsoft.com/dotnet/sdk:7.0

WORKDIR /usr/src/app
COPY ./IronMonkey.Api/ /usr/src/app
RUN dotnet tool install --global dotnet-ef
RUN dotnet restore

ENTRYPOINT [ "dotnet", "watch", "run", "--no-restore"]