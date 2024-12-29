# kraken-dca-bot - Made in Switzerland
## LEGAL-DISCLAIMER
You run this bot completely at your own risk! I won't take any responsibility for what happens to you and your money/crypto.
Do not share your api keys and any privat details!

## Table of Content

1. [Overview](#overview)
2. [Features](#features)
    - [DCA - Service](#dca---service)
    - [Mail - Service](#mail---service)
3. [Installation](#installation)
5. [Miscellaneous](#miscellaneous)
    - [Tipps](#tipps)
    - [Contributing](#contributing)
    - [Bug Report](#bug-report)
6. [Donation](#donation)
7. [License](#license)
8. [Acknowledgements](#acknowledgements)

## Overview
kraken-dca-bot is an open-source Dollar-Cost Averaging (DCA) bot for the Kraken cryptocurrency exchange. It allows users to automate their cryptocurrency investments by periodically purchasing a fixed amount of a chosen cryptocurrency. It is designed to run in a docker container 24/7. Mine runs on a Raspberry Pi 4 headless with VS Code Remote for development and deploying.

## Features
- Automated DCA strategy
- Configurable crypto amount
- Support for multiple cryptocurrencies (Tested only with Bitcoin)
- Secure API key management
- Open-Source: not your code not your coins. ;-)

Optional Features: 
- Notification Bot:
    - Daily Mail to see what the bot has invested in the last 24h
    - Sqlite DB with all closed orders

### DCA - Service
The dca service runs inside a docker container. It can be configured through environment variables through docker compose. Your secrets will be injected through docker secrets. 

The dca services works best in a seperate kraken account which you do not make any other trades. If you also want to trade make a new kraken account just for this bot.

The service works under the assumption that you top up your kraken account every month at the same date (vaction days and weekends are taken into considaration with CultureOptions.)

#### How it works:
1. It checks your balance
2. It checks the current crypto price
3. It checks how long it takes until the next top up should happen.
4. Based on this data and the OrderOptions it calculates the interval to buy your desired crypto if the price would stay the same.
5. It waits until this interval time is over and recalculates step 1-4 
6. It makes an order and waits the min wait time then starts over with step 1.


### Mail - Service
The mail service runs inside a docker container. It can be configured through environment variables through docker compose. Your secrets will be injected through docker secrets. 

The mail service waits until your desired time (hour of day) then fetches all closed orders starting from the last fetched closed order. These are then stored in the SQLite db (Kraken.db).
Based on this orders an html mail will be constructed with the orders which are closed in the last 24 hours and then sents it to you.

Then it will wait about 24 hours until the next mail should be sent.
(This design was chosen over a cronjob, because there will be more features in the future, like responsing to mail/rcs commands...)

### Installation
Please have a look at the wiki [https://github.com/Zuricos/dotnet-kraken-dca-bot/wiki/Installation](https://github.com/Zuricos/dotnet-kraken-dca-bot/wiki/Installation)

## Miscellaneous
### Tipps
Create a Subaccount for the DCA and create the api keys for it. If you want to trade without the bot intercept your trading wallet and use the money which is designed for trading.
-> See [https://docs.kraken.com/api/docs/rest-api/create-subaccount](https://docs.kraken.com/api/docs/rest-api/create-subaccount)

### Contributing
Contributions are welcome! Just reach out to me over an issue or the like.

### Bug Report
If you encounter a bug. Feel free to open an issue. As I ran this by myself I am interested into keeping it bug free.
Please provide further information like your configuration (WITHOUT SECRETS!) and logs from the issue (You can delete the sensible informations)

## Donation
If you find this project valuable and it saves you some dimes or hassle, feel free to donate a few satoshis to support my work. Your contributions are greatly appreciated and help me continue to improve and maintain the project.

### Bitcoin Donation

You can send Bitcoin to the following address:

<img src="btc_adress_qr.png" alt="Bitcoin Donation QR Code" width="200">

```
bc1qaaskkfljrd3fquq5vzzfjsqvh48fq0jus8k8e4
```
Thank you for your support! I am truly grateful!

## License
This project is licensed under a custom license - see the [LICENSE](LICENSE) file for details.

## Acknowledgements
- [Kraken API](https://www.kraken.com/features/api)
- [Serilog](https://serilog.net/)
- [Nager Date](https://github.com/nager/Nager.Date)
