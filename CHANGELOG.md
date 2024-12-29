# Changelog

All notable changes to this project will be documented in this file.

## 2024/12/29 - v1.1.0
MailService:
- Renamed Worder to DailyReporter
- Added Summary to mail
- Added Mail with hints and issue linkage when no orders where made in last 24 hours.

MailService.Test:
- Fixed usage of sqlite, instead of using InMemory

## RELEASE - 2024/12/26 - v1.0.1
DcaService:
- Fix calculation of next topup, which occured on first start of container near configured top up day of month.

## 2024/12/26 - v1.0.0
Deployment:
- adjusted Docker Compose to work with github container registry

## 2024/12/26 - v0.1.2
MailService:
- made orders in mail descending on timestamp

## 2024/12/25 - v0.1.1
Fix of pipeline

## 2024/12/25 - v0.1.0
Prerelease with pipeline for Docker

- MailService with Daily Report
- DcaService which buys the configured crypto in calculated intervals. 
