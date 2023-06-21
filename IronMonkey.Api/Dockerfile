FROM mcr.microsoft.com/dotnet/sdk:7.0.302

RUN apt-get update
RUN apt-get install -y unzip
RUN curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l ~/vsdbg

WORKDIR /usr/src/app
COPY ./IronMonkey.Api/ /usr/src/app

ENV ASPNETCORE_URLS=http://*:5002
ENV ASPNETCORE_ENVIRONMENT=Development

RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"
# RUN dotnet restore
# RUN dotnet build -c Debug --no-restore

ENTRYPOINT [ "dotnet", "watch", "run"]