using FluentValidation;
using Gatherly.Application.Behaviors;
using Gatherly.Domain.Repositories;
using Gatherly.Infrastructure.BackgroundJobs;
using Gatherly.Infrastructure.Idempotence;
using Gatherly.Persistence;
using Gatherly.Persistence.Interceptors;
using Gatherly.Persistence.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Scrutor;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IMemberRepository, MemberRepository>();
builder.Services.Decorate<IMemberRepository, CachedMemberRepository>();

builder
    .Services
    .Scan(
        selector => selector
            .FromAssemblies(
                Gatherly.Infrastructure.AssemblyReference.Assembly,
                Gatherly.Persistence.AssemblyReference.Assembly)
            .AddClasses(false)
            .UsingRegistrationStrategy(RegistrationStrategy.Skip)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

builder.Services.AddMemoryCache();

builder.Services.AddMediatR(Gatherly.Application.AssemblyReference.Assembly);

builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));

builder.Services.Decorate(typeof(INotificationHandler<>), typeof(IdempotentDomainEventHandler<>));

builder.Services.AddValidatorsFromAssembly(
    Gatherly.Application.AssemblyReference.Assembly,
    includeInternalTypes: true);

string connectionString = builder.Configuration.GetConnectionString("Database");

builder.Services.AddSingleton<ConvertDomainEventsToOutboxMessagesInterceptor>();

builder.Services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

builder.Services.AddDbContext<ApplicationDbContext>(
    (sp, optionsBuilder) =>
    {
        var outboxInterceptor = sp.GetService<ConvertDomainEventsToOutboxMessagesInterceptor>()!;
        var auditableInterceptor = sp.GetService<UpdateAuditableEntitiesInterceptor>()!;

        optionsBuilder.UseSqlServer(connectionString)
            .AddInterceptors(
                outboxInterceptor,
                auditableInterceptor);
    });

builder.Services.AddQuartz(configure =>
{
    var jobKey = new JobKey(nameof(ProcessOutboxMessagesJob));

    configure
        .AddJob<ProcessOutboxMessagesJob>(jobKey)
        .AddTrigger(
            trigger =>
                trigger.ForJob(jobKey)
                    .WithSimpleSchedule(
                        schedule =>
                            schedule.WithIntervalInSeconds(100)
                                .RepeatForever()));

    configure.UseMicrosoftDependencyInjectionJobFactory();
});

builder.Services.AddQuartzHostedService();

builder
    .Services
    .AddControllers()
    .AddApplicationPart(Gatherly.Presentation.AssemblyReference.Assembly);

builder.Services.AddSwaggerGen();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
