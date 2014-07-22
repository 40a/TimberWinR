﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ctstone.Redis;
using Newtonsoft.Json.Linq;
using NLog;
using System.Threading.Tasks;
using RapidRegex.Core;
using System.Text.RegularExpressions;
using System.Globalization;

namespace TimberWinR.Outputs
{
    public class RedisOutput : OutputSender
    {
        private readonly string _logstashIndexName;
        private readonly int _port;
        private readonly int _timeout;
        private readonly object _locker = new object();
        private readonly List<string> _jsonQueue;
        readonly Task _consumerTask;
        private readonly string[] _redisHosts;
        private int _redisHostIndex;
        private TimberWinR.Manager _manager;

        /// <summary>
        /// Get the next client
        /// </summary>
        /// <returns></returns>
        private RedisClient getClient()
        {
            if (_redisHostIndex >= _redisHosts.Length)
                _redisHostIndex = 0;

            int numTries = 0;
            while (numTries < _redisHosts.Length)
            {
                try
                {
                    RedisClient client = new RedisClient(_redisHosts[_redisHostIndex], _port, _timeout);

                    _redisHostIndex++;
                    if (_redisHostIndex >= _redisHosts.Length)
                        _redisHostIndex = 0;

                    return client;
                }
                catch (Exception ex)
                {

                }
                numTries++;
            }

            return null;
        }

        public RedisOutput(TimberWinR.Manager manager, string[] redisHosts, CancellationToken cancelToken, string logstashIndexName = "logstash", int port = 6379, int timeout = 10000)
            : base(cancelToken)
        {
            _manager = manager;
            _redisHostIndex = 0;
            _redisHosts = redisHosts;
            _jsonQueue = new List<string>();
            _port = port;
            _timeout = timeout;
            _logstashIndexName = logstashIndexName;
            _consumerTask = new Task(RedisSender, cancelToken);
            _consumerTask.Start();
        }


        /// <summary>
        /// Forward on Json message to Redis Logstash queue
        /// </summary>
        /// <param name="jsonMessage"></param>
        protected override void MessageReceivedHandler(JObject jsonMessage)
        {
            if (_manager.Config.Groks != null)
                ProcessGroks(jsonMessage);

            var message = jsonMessage.ToString();
            LogManager.GetCurrentClassLogger().Info(message);

            lock (_locker)
            {
                _jsonQueue.Add(message);
            }
        }

        private void ProcessGroks(JObject json)
        {
            foreach (var grok in _manager.Config.Groks)
            {
                JToken token = null;
                if (json.TryGetValue(grok.Field, StringComparison.OrdinalIgnoreCase, out token))
                {
                    string text = token.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        string expr = grok.Match;
                        var resolver = new RegexGrokResolver();
                        var pattern = resolver.ResolveToRegex(expr);
                        var match = Regex.Match(text, pattern);
                        if (match.Success)
                        {
                            var regex = new Regex(pattern);
                            var namedCaptures = regex.MatchNamedCaptures(text);
                            foreach (string fieldName in namedCaptures.Keys)
                            {

                                if (fieldName == "timestamp")
                                {
                                    string value = namedCaptures[fieldName];
                                    DateTime ts;
                                    if (DateTime.TryParse(value, out ts))
                                        json.Add(fieldName, ts.ToUniversalTime());
                                    else if (DateTime.TryParseExact(value, new string[] 
                                                { 
                                                    "MMM dd hh:mm:ss", 
                                                    "MMM dd HH:mm:ss", 
                                                    "MMM dd h:mm",
                                                    "MMM dd hh:mm",                  
                                                }, CultureInfo.InvariantCulture, DateTimeStyles.None, out ts))
                                        json.Add(fieldName, ts.ToUniversalTime());
                                    else
                                        json.Add(fieldName, (JToken)namedCaptures[fieldName]);
                                }
                                else
                                    json.Add(fieldName, (JToken)namedCaptures[fieldName]);
                            }
                        }
                    }
                }
            }
        }

        // 
        // Pull off messages from the Queue, batch them up and send them all across
        // 
        private void RedisSender()
        {
            while (!CancelToken.IsCancellationRequested)
            {
                string[] messages;
                lock (_locker)
                {
                    messages = _jsonQueue.ToArray();
                    _jsonQueue.Clear();
                }

                if (messages.Length > 0)
                {
                    int numHosts = _redisHosts.Length;
                    while (numHosts-- > 0)
                    {
                        try
                        {
                            // Get the next client
                            using (RedisClient client = getClient())
                            {
                                if (client != null)
                                {
                                    client.StartPipe();

                                    foreach (string jsonMessage in messages)
                                    {
                                        try
                                        {
                                            client.RPush(_logstashIndexName, jsonMessage);
                                        }
                                        catch (SocketException)
                                        {
                                        }
                                    }
                                    client.EndPipe();
                                    break;
                                }
                                else
                                {
                                    LogManager.GetCurrentClassLogger()
                                        .Fatal("Unable to connect with any Redis hosts, {0}",
                                            String.Join(",", _redisHosts));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogManager.GetCurrentClassLogger().Error(ex);
                        }
                    }
                }
                System.Threading.Thread.Sleep(1000);
            }
        }
    }
}
