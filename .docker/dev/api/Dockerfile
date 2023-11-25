FROM mcr.microsoft.com/dotnet/sdk:8.0

WORKDIR /usr/src/app
COPY ./IronMonkey.Api/ /usr/src/app
RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet restore
RUN dotnet build -c Debug

USER $APP_UID
ENTRYPOINT [ "dotnet", "watch", "run", "--urls=http://*:5000", "--configuration", "Debug", "--no-launch-profile"]
