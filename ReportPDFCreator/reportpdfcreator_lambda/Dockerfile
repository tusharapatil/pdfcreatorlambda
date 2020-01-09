FROM newtmitch/sonar-scanner AS sonar
ARG projectName=ReportPDFCreator
COPY src/. app
RUN sonar-scanner -Dsonar.host.url=http://ies-val-jenkins.ies.mentorg.com:9000 -Dsonar.projectName=$projectName

FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS build
#RUN yum install aws-cli -y
RUN dotnet tool install -g Amazon.Lambda.Tools
RUN dotnet tool install -g dotnet-zip
COPY src/. /src
WORKDIR /src/ReportPDFCreator
RUN /root/.dotnet/tools/dotnet-lambda package

# FROM python:alpine
# ARG CLI_VERSION=1.16.289
# RUN apk -uv add --no-cache groff jq less && \
#     pip install --no-cache-dir awscli==$CLI_VERSION
# COPY --from=build /src/ReportPDFCreator/bin/Release/netcoreapp2.1/* /
# WORKDIR /aws
# CMD sh 