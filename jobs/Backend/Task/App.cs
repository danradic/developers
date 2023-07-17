using ExchangeRateUpdater.Models.Behavior;
using ExchangeRateUpdater.Models.Errors;
using ExchangeRateUpdater.Models.Types;
using ExchangeRateUpdater.Persistence;
using ExchangeRateUpdater.Services;
using Microsoft.Extensions.Logging;
using OneOf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ExchangeRateUpdater;

internal class App
{
    private readonly IExchangeRateRepository _exchangeRateRepository;
    private readonly IExchangeRateProvider _exchangeRateProvider;
    private readonly ILogger<App> _logger;

    private IEnumerable<Currency> _sourceCurrencies; 
    private OneOf<IEnumerable<ExchangeRate>, Error> _exchangeRatesResult;
    private int _resultCode;

    public App(ILogger<App> logger,
        IExchangeRateRepository exchangeRateRepository,
        IExchangeRateProvider exchangeRateProvider)
    {
        _exchangeRateRepository = exchangeRateRepository;
        _exchangeRateProvider = exchangeRateProvider;
        _logger = logger;
    }

    internal async Task<int> Run(string[] args)
    {
        try
        {
            return await GetSourceCurrencies()
                .GetExchangeRates().Result
                .PrintExchangeRates()
                .OrPrintValidationError()
                .GetResultCode();
        }
        catch (Exception ex)
        {
            return await PrintExceptionMessage(ex)
            .LogExceptionError(ex)
            .GetResultCode();
        }
    }

    private App GetSourceCurrencies()
    {
        _sourceCurrencies = _exchangeRateRepository.GetSourceCurrencies();
        return this;
    }

    private async Task<App> GetExchangeRates()
    {
        _exchangeRatesResult = await _exchangeRateProvider.GetExchangeRates(_sourceCurrencies);
        return this;
    }

    private App PrintExchangeRates()
    {
        if (!_exchangeRatesResult.IsT0)
        { 
            _resultCode = -1;
            return this;
        }

        var rates = _exchangeRatesResult.AsT0;

        Console.WriteLine($"Successfully retrieved {rates.Count()} exchange rates:");
        foreach (var rate in rates)
        {
            Console.WriteLine(rate.ToStringFormat());
        }
        return this;
    }

    private App OrPrintValidationError()
    {
        if (!_exchangeRatesResult.IsT1) return this;

        var error = _exchangeRatesResult.AsT1;

        Console.WriteLine(error.ToString());

        return this;
    }

    private App PrintExceptionMessage(Exception ex)
    {
        Console.WriteLine($"Could not retrieve exchange rates: '{ex.Message}'.");
        return this;
    }

    private App LogExceptionError(Exception ex)
    {
        _logger.LogError(ex, "Unhandled exception occured.");
        _resultCode = -2;
        return this;
    }

    private async Task<int> GetResultCode() => await Task.FromResult(_resultCode);
}

