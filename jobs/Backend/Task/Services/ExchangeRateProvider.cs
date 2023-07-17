using ExchangeRateUpdater.ApiClients.CzechNationalBank;
using ExchangeRateUpdater.ApiClients.Responses;
using ExchangeRateUpdater.Mappings;
using ExchangeRateUpdater.Models.Errors;
using ExchangeRateUpdater.Models.Time;
using ExchangeRateUpdater.Models.Types;
using ExchangeRateUpdater.Utilities;
using OneOf;
using Refit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ExchangeRateUpdater.Services;

internal class ExchangeRateProvider : IExchangeRateProvider
{
    private const string TargetCurrency = "CZK";

    private readonly IExchangeRateApiClient _exchangeRateApiClient;


    private IEnumerable<Currency> _sourceCurrencies;
    private ApiResponse<GetExchangeRatesResponse> _apiDailyResponse;
    private ApiResponse<GetExchangeRatesResponse> _apiOtherResponse;
    private bool _isSourceCurrenciesValid;
    private bool _isApiDailyResponseSuccess;
    private bool _isApiOtherResponseSuccess;

    public ExchangeRateProvider(IExchangeRateApiClient exchangeRateApiClient)
    {
        _exchangeRateApiClient = exchangeRateApiClient;
    }

    public async Task<OneOf<IEnumerable<ExchangeRate>, Error>> GetExchangeRates(IEnumerable<Currency> sourceCurrencies)
    {
        return await ValidateSourceCurrencies(sourceCurrencies)
            .GetExchangeRateApiResponse().Result
            .ValidateExchangeRateApiResponses()
            .HandleResult();
    }

    private static Func<ExchangeRateApiItem, bool> IsCurrencyApiItemAmongSourceCurrencies(IEnumerable<Currency> sourceCurrencies) =>
        exchangeRateApiItem => sourceCurrencies.Any(IsMatch(exchangeRateApiItem));

    private static Func<Currency, bool> IsMatch(ExchangeRateApiItem exchangeRateApiItem) =>
        sourceCurrency => sourceCurrency.Code.Value == exchangeRateApiItem.CurrencyCode;

    private ExchangeRateProvider ValidateSourceCurrencies(IEnumerable<Currency> sourceCurrencies)
    {
        if (sourceCurrencies.IsAny())
        {
            _isSourceCurrenciesValid = true;
            _sourceCurrencies = sourceCurrencies;
        }
        return this;
    }

    private async Task<ExchangeRateProvider> GetExchangeRateApiResponse()
    {
        if (!_isSourceCurrenciesValid) return this;

        var apiDailyResponseTask = _exchangeRateApiClient.GetDaily();
        var apiOtherResponseTask = _exchangeRateApiClient.GetOtherByYearMonth(DateTime.UtcNow.GetYearMonthString());

        await Task.WhenAll(apiDailyResponseTask, apiOtherResponseTask);

        _apiDailyResponse = await apiDailyResponseTask;
        _apiOtherResponse = await apiOtherResponseTask;

        return this;
    }

    private ExchangeRateProvider ValidateExchangeRateApiResponses()
    {
        if (!_isSourceCurrenciesValid) return this;

        if (!_apiDailyResponse.IsSuccessStatusCode) return this;
        _isApiDailyResponseSuccess = true;

        if (!_apiOtherResponse.IsSuccessStatusCode) return this;
        _isApiOtherResponseSuccess = true;

        return this;
    }

    private async Task<OneOf<IEnumerable<ExchangeRate>, Error>> HandleResult()
    {
        if (!_isSourceCurrenciesValid)
            return new Error(errorType: ErrorType.ValidationError)
                .WithMessage("Source currencies list not provided while getting exchange rates.");

        if (!_isApiDailyResponseSuccess)
            return new Error(errorType: ErrorType.ApiError)
                .WithMessage($"{_apiDailyResponse.GetEndpointUrl()} failed with message: {_apiDailyResponse.Error.Message}.");

        if (!_isApiOtherResponseSuccess)
            return new Error(errorType: ErrorType.ApiError)
                .WithMessage($"{_apiOtherResponse.GetEndpointUrl()} failed with message: {_apiOtherResponse.Error.Message}.");

        return _apiDailyResponse.Content.Rates
            .Concat(_apiOtherResponse.Content.Rates)
            .Where(IsCurrencyApiItemAmongSourceCurrencies(_sourceCurrencies))
            .Select(exchangeRateApiItem => exchangeRateApiItem.ToExchangeRateResult(TargetCurrency))
            .ToList();
    }
}
