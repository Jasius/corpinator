using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CorpinatorBot
{
    public class DiscordBotHost
    {
        private IConfigurationBuilder _configurationBuilder;
        private Action<ILoggingBuilder> _loggingBuilder;
        private IServiceCollection _serviceCollection;
        private Type _discordBot;
        private HashSet<Type> _toBind;

        public DiscordBotHost()
        {
            _toBind = new HashSet<Type>();
        }

        internal IServiceProvider ServiceProvider { get; private set; }

        public DiscordBotHost WithServices(IServiceCollection serviceCollection)
        {
            _serviceCollection = serviceCollection;
            return this;
        }

        public DiscordBotHost WithLogging(Action<ILoggingBuilder> loggingBuilder)
        {
            _loggingBuilder = loggingBuilder;
            return this;
        }

        internal DiscordBotHost WithBinding<T>() where T : class
        {
            var type = typeof(T);
            if (!_toBind.Contains(type))
                _toBind.Add(type);

            return this;
        }

        public DiscordBotHost WithConfiguration(IConfigurationBuilder configBuilder)
        {
            _configurationBuilder = configBuilder;
            return this;
        }

        public DiscordBotHost WithDiscordBot<T>() where T: IDiscordBot
        {
            _discordBot = typeof(T);
            return this;
        }

        public async Task Run(CancellationToken token = default)
        {
            if (_discordBot == null)
            {
                throw new InvalidOperationException("Bot class is not set. Execute WithDiscordBot prior to Run.");
            }

            if (_loggingBuilder == null)
            {
                _loggingBuilder = builder => builder.AddConsole();
            }
            
            if (_configurationBuilder == null)
            {
                _configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true);
            }

            if (_serviceCollection == null)
            {
                _serviceCollection = new ServiceCollection();
            }

            var configuration = _configurationBuilder.Build();
            _serviceCollection.AddSingleton<IConfiguration>(configuration);

            foreach (var item in _toBind)
            {
                    var section = item.Name;
                    var instance = Activator.CreateInstance(item);
                    if (instance == null) continue;

                    configuration.Bind(section, instance);
                    _serviceCollection.AddSingleton(item, instance);   
            }
            _serviceCollection.AddLogging(_loggingBuilder);

            ServiceProvider = _serviceCollection.BuildServiceProvider();

            var bot = ActivatorUtilities.CreateInstance(ServiceProvider, _discordBot, ServiceProvider) as IDiscordBot;

            await bot.Start();

            try
            {
                await Task.Delay(-1, token);
            }
            catch { }

            await bot.Stop();

            if (bot is IDisposable disposableBot)
            {
                disposableBot.Dispose();
            }
        }
    }
}