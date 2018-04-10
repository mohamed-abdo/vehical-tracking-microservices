﻿using BuildingAspects.Behaviors;
using BuildingAspects.Functors;
using BuildingAspects.Utilities;
using DomainModels.DataStructure;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BackgroundMiddleware
{
    /// <summary>
    /// rabbitMQ worker listener background service. 
    /// </summary>
    /// <typeparam name="TRequest">Expected structure coming from the publisher to the subscriber.</typeparam>
    public class RabbitMQSubscriberWorker<TRequest> : BackgroundService
    {
        private readonly ILogger logger;
        private int defaultMiddlewarePort = 5672;//default rabbitmq port
        private readonly RabbitMQConfiguration hostConfig;
        private readonly IConnectionFactory connectionFactory;
        //Design decision: keep/ delegate responsibility of translating and casting object to the target type, to receiver callback, even exception will be thrown in his execution thread.
        private readonly Action<Func<TRequest>> callback;
        /// <summary>
        /// internal construct subscriber object
        /// </summary>
        /// <param name="logger">ILogger instance</param>
        /// <param name="hostConfig">rabbitMQ configuration</param>
        private RabbitMQSubscriberWorker(ILoggerFactory logger, RabbitMQConfiguration hostConfig, Action<Func<TRequest>> callback)
        {
            this.logger = logger?
                            .AddConsole()
                            .AddDebug()
                            .CreateLogger<RabbitMQPublisher>()
                            ?? throw new ArgumentNullException("Logger reference is required");

            Validators.EnsureHostConfig(hostConfig);
            this.hostConfig = hostConfig;
            this.callback = callback ?? throw new ArgumentNullException("Callback reference is invalid");
            var host = Helper.ExtractHostStructure(this.hostConfig.hostName);
            connectionFactory = new ConnectionFactory()
            {
                HostName = host.hostName,
                Port = host.port ?? defaultMiddlewarePort,
                UserName = hostConfig.userName,
                Password = hostConfig.password,
                ContinuationTimeout = TimeSpan.FromSeconds(DomainModels.System.Identifiers.TimeoutInSec)
            };
        }

        /// <summary>
        /// factory constructor for subscriber object
        /// </summary>
        /// <param name="logger">ILogger instance</param>
        /// <param name="hostConfig">rabbitMQ configuration</param>
        public static RabbitMQSubscriberWorker<TRequest> Create(ILoggerFactory logger, RabbitMQConfiguration hostConfig, Action<Func<TRequest>> callback)
        {
            return new RabbitMQSubscriberWorker<TRequest>(logger, hostConfig, callback);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await new Function(logger, DomainModels.System.Identifiers.RetryCount).Decorate(() =>
             {
                 using (var connection = connectionFactory.CreateConnection())
                 using (var channel = connection.CreateModel())
                 {
                     channel.ExchangeDeclare(exchange: hostConfig.exchange, type: ExchangeType.Topic, durable: true);
                     //TODO: in case scaling the middleware, running multiple workers simultaneously. 
                     channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                     var queueName = channel.QueueDeclare().QueueName;

                     foreach (var bindingKey in hostConfig.routes)
                     {
                         channel.QueueBind(queue: queueName,
                                           exchange: hostConfig.exchange,
                                           routingKey: bindingKey);
                     }

                     logger.LogInformation("[*] Waiting for messages.");

                     var consumer = new EventingBasicConsumer(channel);

                     consumer.Received += async (model, ea) =>
                      {
                          await new Function(logger, DomainModels.System.Identifiers.RetryCount).Decorate(() =>
                          {
                              if (ea.Body == null || ea.Body.Length == 0)
                                  throw new TypeLoadException("Invalid message type");

                              // callback action feeding 
                              if (Utilities.BinaryDeserialize(ea.Body) is TRequest request)
                                  callback(() => request);
                              else
                                  throw new InvalidCastException("Invalid message cast");
                              //send acknowledgment to publisher

                              channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);

                              logger.LogInformation($"[x] Event sourcing service receiving a messaged from exchange: {hostConfig.exchange}, route :{ea.RoutingKey}.");
                              return true;
                          }, (ex) =>
                          {
                              switch (ex)
                              {
                                  case TypeLoadException typeEx:
                                      return true;
                                  default:
                                      return false;
                              }
                          });
                      };
                     //bind event handler
                     channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
                     Console.ReadLine();
                     return Task.CompletedTask;
                 }
             }, (ex) =>
             {
                 switch (ex)
                 {
                     case BrokerUnreachableException brokerEx:
                         return true;
                     case ConnectFailureException connEx:
                         return true;
                     case SocketException socketEx:
                         return true;
                     default:
                         return false;
                 }
             });
        }
    }
}
