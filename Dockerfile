# Build the sample server together with the library it depends on.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ToolCraft.Mcp/ src/ToolCraft.Mcp/
COPY samples/IncidentSandbox/ samples/IncidentSandbox/

RUN dotnet publish samples/IncidentSandbox/IncidentSandbox.csproj -c Release -o /app

# Default entrypoint is stdio, which is what MCP introspection expects.
# The same image serves stateless streamable HTTP if you pass --http.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "IncidentSandbox.dll"]
