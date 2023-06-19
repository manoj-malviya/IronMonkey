FROM mcr.microsoft.com/dotnet/sdk:7.0.302

RUN apt-get update
RUN apt-get install -y unzip
RUN curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l ~/vsdbg

WORKDIR /usr/src/app
COPY ./IronMonkey.Api/ /usr/src/app
RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet restore
RUN dotnet build -c Debug

ENTRYPOINT [ "dotnet", "watch", "run", "--no-restore", "--configuration", "Debug", "--no-launch-profile"]