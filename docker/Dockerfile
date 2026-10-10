FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Sound2Text.Api/Sound2Text.Api.csproj src/Sound2Text.Api/
RUN dotnet restore src/Sound2Text.Api
COPY src/ src/
RUN dotnet publish src/Sound2Text.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS dotnet

# Whisper.net 1.9.x CUDA runtime links libcudart.so.13 (also covers Blackwell / RTX 50xx)
FROM nvidia/cuda:13.0.2-cudnn-runtime-ubuntu24.04
RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg ca-certificates libicu74 libssl3 \
 && rm -rf /var/lib/apt/lists/*
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet \
    PATH=/usr/share/dotnet:$PATH \
    ASPNETCORE_URLS=http://+:8080 \
    NVIDIA_DRIVER_CAPABILITIES=compute,utility \
    Whisper__ModelPath=/models/ggml-large-v3-q5_0.bin \
    Whisper__DataDir=/data
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Sound2Text.Api.dll"]
