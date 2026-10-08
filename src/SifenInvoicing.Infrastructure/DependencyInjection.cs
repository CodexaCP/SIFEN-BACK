using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SifenInvoicing.Application.Auditing;
using SifenInvoicing.Application.Auth;
using SifenInvoicing.Application.Diagnostics;
using SifenInvoicing.Application.Invoices;
using SifenInvoicing.Application.Onboarding;
using SifenInvoicing.Application.Operations;
using SifenInvoicing.Application.Platform;
using SifenInvoicing.Application.Security;
using SifenInvoicing.Application.Sifen;
using SifenInvoicing.Application.Tenancy;
using SifenInvoicing.Application.XmlDe;
using SifenInvoicing.Application.XmlSigning;
using SifenInvoicing.Application.XmlValidation;
using SifenInvoicing.Infrastructure.Auditing;
using SifenInvoicing.Infrastructure.Auth;
using SifenInvoicing.Infrastructure.Diagnostics;
using SifenInvoicing.Infrastructure.Invoices;
using SifenInvoicing.Infrastructure.Onboarding;
using SifenInvoicing.Infrastructure.Operations;
using SifenInvoicing.Infrastructure.Platform;
using SifenInvoicing.Infrastructure.Persistence;
using SifenInvoicing.Infrastructure.Security;
using SifenInvoicing.Infrastructure.Sifen;
using SifenInvoicing.Infrastructure.Tenancy;
using SifenInvoicing.Infrastructure.XmlSigning;
using SifenInvoicing.Infrastructure.XmlValidation;

using SifenInvoicing.Application.Fiscal;
using SifenInvoicing.Application.Numbering;
using SifenInvoicing.Infrastructure.Fiscal;
using SifenInvoicing.Infrastructure.Numbering;

namespace SifenInvoicing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<ITenantContextAccessor, AsyncLocalTenantContextAccessor>();
        services.AddScoped<IAuditTrail, LoggerAuditTrail>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IPlatformAuthService, PlatformAuthService>();
        services.AddScoped<IPlatformAuthBootstrapper, PlatformAuthBootstrapper>();
        services.AddScoped<IPlatformCompanyService, EfPlatformCompanyService>();
        services.AddScoped<IOperationalReadinessReporter, ConfigurationOperationalReadinessReporter>();
        services.AddScoped<IFeTenantDiagnosticService, EfFeTenantDiagnosticService>();
        services.AddScoped<ITenantOnboardingService, EfTenantOnboardingService>();
        services.AddScoped<ITenantSecretProvider, LocalConfigurationTenantSecretProvider>();
        services.AddScoped<ITenantCertificateValidator, LocalTenantCertificateValidator>();
        services.AddScoped<IFacturaXmlGenerator, FacturaXmlGenerator>();
        services.AddScoped<IFacturaXmlPreSubmissionValidator, FacturaXmlPreSubmissionValidator>();
        services.AddScoped<IInvoiceKudePdfRenderer, InvoiceKudePdfRenderer>();
        services.AddScoped<IInvoiceService, EfInvoiceService>();
        services.AddScoped<INumberingService, EfNumberingService>();
        services.AddSingleton<IFiscalClock, OffsetFiscalClock>();
        services.AddScoped<IFeTraceService, EfFeTraceService>();
        services.AddScoped<IFeInvoiceTestFlowService, EfFeInvoiceTestFlowService>();
        services.AddScoped<ISifenSoapTransport, DefaultSifenSoapTransport>();
        services.AddScoped<ISifenSubmissionGateway, ConfigurationSifenSubmissionGateway>();
        services.AddScoped<ISifenResponseParser, DefaultSifenResponseParser>();
        services.AddScoped<IXmlDocumentSigner, SifenXmlDocumentSigner>();
        services.AddScoped<IXmlSchemaValidator, XmlSchemaValidator>();
        services.AddScoped<ISifenDeXsdValidator, SifenDeXsdValidator>();
        services.AddSingleton<SifenInvoicing.Application.Qr.ISifenQrBuilder, SifenInvoicing.Application.Qr.SifenQrBuilder>();
        services.AddScoped<SifenInvoicing.Application.Qr.ISifenDeQrAttacher, SifenInvoicing.Infrastructure.Qr.SifenDeQrAttacher>();
        services.AddSingleton(_ => SifenDeBuilderOptionsFactory.Create(configuration));
        services.AddSingleton<SifenDeXmlBuilder>();
        services.AddDbContext<SifenDbContext>(options =>
        {
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
        });

        return services;
    }
}
