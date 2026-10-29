
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY ["NotificationSystem.csproj","./"] 
RUN dotnet restore
COPY . . 
RUN dotnet publish -c Release -o /output

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS Run
WORKDIR /app
COPY --from=build /output .
ENTRYPOINT ["dotnet","NotificationSystem.dll"]