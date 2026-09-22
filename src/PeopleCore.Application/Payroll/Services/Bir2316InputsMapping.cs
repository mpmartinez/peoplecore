using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>Between the saved row and the request-shaped <see cref="Bir2316ManualInputs"/>.</summary>
public static class Bir2316InputsMapping
{
    public static Bir2316ManualInputs ToManualInputs(this Bir2316Inputs e) => new()
    {
        PrevEmployerTin = e.PrevEmployerTin,
        PrevEmployerName = e.PrevEmployerName,
        PrevEmployerAddress = e.PrevEmployerAddress,
        PrevEmployerZipCode = e.PrevEmployerZipCode,
        Item22_PrevTaxableCompensation = e.Item22_PrevTaxableCompensation,
        Item25B_PrevTaxWithheld = e.Item25B_PrevTaxWithheld,
        Item27_PeraTaxCredit = e.Item27_PeraTaxCredit,
        Item35_DeMinimis = e.Item35_DeMinimis,
        Item33_HazardPayMwe = e.Item33_HazardPayMwe,
        StatutoryMinWagePerDay = e.StatutoryMinWagePerDay,
        StatutoryMinWagePerMonth = e.StatutoryMinWagePerMonth
    };

    public static void Apply(this Bir2316Inputs e, Bir2316ManualInputs m)
    {
        e.PrevEmployerTin = m.PrevEmployerTin;
        e.PrevEmployerName = m.PrevEmployerName;
        e.PrevEmployerAddress = m.PrevEmployerAddress;
        e.PrevEmployerZipCode = m.PrevEmployerZipCode;
        e.Item22_PrevTaxableCompensation = m.Item22_PrevTaxableCompensation;
        e.Item25B_PrevTaxWithheld = m.Item25B_PrevTaxWithheld;
        e.Item27_PeraTaxCredit = m.Item27_PeraTaxCredit;
        e.Item35_DeMinimis = m.Item35_DeMinimis;
        e.Item33_HazardPayMwe = m.Item33_HazardPayMwe;
        e.StatutoryMinWagePerDay = m.StatutoryMinWagePerDay;
        e.StatutoryMinWagePerMonth = m.StatutoryMinWagePerMonth;
    }
}
