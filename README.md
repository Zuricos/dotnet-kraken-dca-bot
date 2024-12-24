# kraken-dca-bot - Made in Switzerland
## LEGAL-DISCLAIMER:
You run this bot completely at your own risk! I won't take any responsibility for what happens to you and your money/crypto.
Do not share your api keys and any privat details!

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

## Prerequisites
- .NET 8.0 SDK
- Kraken Pro API Keys
    - my-dca-bot: 
        - Funds Permission: Query
        - Orders and Trades: Create & Modify orders
    - my-notification-bot:
        - Funds Permission: Query
        - Orders and Trades: Query open orders & trades
        - Orders and Trades: Query closed orders & trades
- Google Account with 16 char Application Password. (Required for mail-service)

## Installation
1. Clone the repository:
    ```bash
    git clone https://github.com/Zuricos/kraken-dca-bot.git
    cd kraken-dca-bot
    ```
2. Open the docker_compose.yaml and set the environment variable to your liking. Look [Configuration](#configuration) for explanation of the configs.

    a. If notification bot is not what you want, removed it from the yaml file as it is optional

3. Copy each docker.*.secrets-template.json like:

    ```bash
    cp docker.dca.secrets-template.json docker.dca.secrets.json
    ```
    
    And fill them with your secrets. 
    Note: As long as a file ends with *secrets.json it is ignored in git.

4. Run 
    ```bash
    docker-compose up --build --detached
    ```

5. Make a standing order to kraken for the default topup day of month.

## Configuration
Here are the configuration explained. For deployment set them to your liking in the docker compose. For development set them in a .env file in the root directory of the repo.
```bash
OrderOptions__Type=Limit # Limit or Market supported
OrderOptions__Fee=0.4   # in percent
OrderOptions__MinOrderVolume=0.00005 # Order Size of the Crypto to buy, 0.00005 is min. order size for bitcoin on kraken
OrderOptions__AskMultiplier=1.00001 # Multiplier for the current ask price. When using limit order.
OrderOptions__CryptoPair=XBTCHF # crypto pair to buy on kraken see https://docs.kraken.com/api/docs/rest-api/get-tradable-asset-pairs/

BalanceOptions__DefaultTopupDayOfMonth=26 # default day where you top up your kraken portfolio
BalanceOptions__ReserveFiat=100 # amount of fiat you'd like to not have the bot automatically invested

WaitOptions__MinWaitTime=00:00:10 # min wait time between check intervals
WaitOptions__MaxWaitTime=01:00:00 # max wait time between check intervals

CultureOptions__CultureString=de-CH # your culture code. Use it for the region where you live. -> is used to determine holidays for calculating the next top up
CultureOptions__CountyCode=CH-ZH # same as above but for the county look up on date.nager.at
CultureOptions__Fiat=CHF # your currency for logging and potential other features

Secrets__ApiKey=<your api public key> # the public key generated on kraken pro
Secrets__ApiSecret=<your api private key> # the private key generated on kraken pro

MailSecrets__SenderEmail=<EmailAdress> # email adress of the sending google account
MailSecrets__GoogleAppPassword=<16charPassword> # the 16 char google application password whithout whitespaces
MailSecrets__ReceiverEmail=<EmailAdress> #your email adress 

MailOptions__HourOfDay=6 # At which time you like to get the mail (24h format UTC) Default to 6 o'clok if not set
MailOptions__CryptoPair=XBTCHF #same as in OrderOptions
MailOptions__Crypto=BTC # the crypto your bying (for Mail stuff)
MailOptions__Fiat=CHF # same as in CultureOptions

```

## Development
If you like to run the test or make further development and running the bot local i.e. with 
```bash
 dotnet run
```
or
```bash
 dotnet test
```

Make a copy of thesecrets-template.json in the project folders you like to run. Rename it to secrets.json and fill up your secrets. Note: secrets.json is ignored in git and docker.
And then run the following command in the shell in the project directory:
```bash
cat ./secrets.json | dotnet user-secrets set
```

Afterwards make sure to reset the 'secrets-template.json' to not make a commit with your secrets.

Further information about develoment with secrets can be found here:
[https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0&tabs=linux](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0&tabs=linux)

## Tipps
Create a Subaccount for the DCA and create the api keys for it. If you want to trade without the bot intercept your trading wallet and use the money which is designed for trading.
-> See [https://docs.kraken.com/api/docs/rest-api/create-subaccount](https://docs.kraken.com/api/docs/rest-api/create-subaccount)

## Contributing
Contributions are welcome! Just reach out to me over an issue or the like.

## Bug Report
If you encounter a bug. Feel free to open an issue. As I ran this by myself I am interested into keeping it bug free.
Please provide further information like your configuration (WITHOUT SECRETS!) and logs from the issue (You can delete the sensible informations)

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgements
- [Kraken API](https://www.kraken.com/features/api)
- [Serilog](https://serilog.net/)
- [Nager Date](https://github.com/nager/Nager.Date)