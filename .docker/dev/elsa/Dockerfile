FROM mcr.microsoft.com/dotnet/sdk:7.0.302

WORKDIR /usr/src/app
COPY ./Workflow/ /usr/src/app
RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet restore

ENTRYPOINT [ "dotnet", "watch", "run", "--no-restore"]