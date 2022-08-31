FROM mcr.microsoft.com/dotnet/core/sdk:3.1

RUN mkdir -p /usr/src
WORKDIR /usr/src/

COPY run.sh /usr/src/
COPY Lusid.Sdk.Examples.sln /usr/src/
COPY Lusid.Sdk.Examples/ /usr/src/Lusid.Sdk.Examples/
COPY LusidFeatureReporter/ /usr/src/LusidFeatureReporter/

RUN chmod +x run.sh
ENTRYPOINT ["./run.sh"]