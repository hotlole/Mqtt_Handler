using MQTTnet;
using MQTTnet.Protocol;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Buffers;
using System.Globalization;
using BlazorApp2.Models;

namespace BlazorApp2.Service
{
    public class MqttService : IDisposable
    {
        private readonly ILogger<MqttService> _logger;
        private IMqttClient _mqttClient;
        private bool _isConnected = false;
        private bool _isDisposed = false;

        public string Status { get; private set; } = "Отключено";
        public event Action OnMessageReceived;

        public List<Device> Devices { get; private set; } = new();

        public Dictionary<string, Expression> DeviceExpressions { get; private set; } = new();

        public MqttService(ILogger<MqttService> logger)
        {
            _logger = logger;
            _logger.LogInformation("MqttService создан");
        }

        public async Task InitializeMqttClient()
        {
            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithClientId("BlazorClient")
                .WithTcpServer("185.38.84.34", 1883)
                .WithCleanSession()
                .Build();

            _mqttClient.ConnectedAsync += async e =>
            {
                _isConnected = true;
                Status = "Подключено";
                _logger.LogInformation("MQTT клиент подключен");
                await SubscribeToTopics();
            };

            _mqttClient.DisconnectedAsync += async e =>
            {
                _isConnected = false;
                Status = "Отключено";
                _logger.LogWarning("MQTT клиент отключен");

                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        _logger.LogInformation($"Попытка переподключения {i + 1}...");

                        if (_mqttClient == null || _isDisposed)
                        {
                            _logger.LogInformation("Создание нового MQTT клиента...");
                            _mqttClient = new MqttClientFactory().CreateMqttClient();
                            await InitializeMqttClient();
                            _isDisposed = false;
                        }

                        if (!_mqttClient.IsConnected)
                        {
                            await _mqttClient.ConnectAsync(options);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Status = $"Попытка {i + 1} не удалась: {ex.Message}";
                        _logger.LogError($"Ошибка переподключения: {ex.Message}");
                    }
                }
            };

            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                var payload = e.ApplicationMessage.Payload.ToArray();
                ProcessMqttMessage(e.ApplicationMessage.Topic, payload);
                OnMessageReceived?.Invoke();
                return Task.CompletedTask;
            };

            try
            {
                _logger.LogInformation("Подключение к MQTT серверу...");
                await _mqttClient.ConnectAsync(options);
            }
            catch (Exception ex)
            {
                Status = $"Ошибка соединения: {ex.Message}";
                _logger.LogError($"Ошибка подключения: {ex.Message}");
            }
        }

        public async Task<bool> PublishMessage(string topic, string payload)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                Status = "MQTT клиент не подключен!";
                _logger.LogError("MQTT клиент не подключен!");
                return false;
            }

            try
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _mqttClient.PublishAsync(message);
                _logger.LogInformation($"Сообщение отправлено в {topic}: {payload}");
                return true;
            }
            catch (Exception ex)
            {
                Status = $"Ошибка отправки сообщения: {ex.Message}";
                _logger.LogError($"Ошибка отправки: {ex.Message}");
                return false;
            }
        }

        private async Task SubscribeToTopics()
        {
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic("test/esp32/calculator/status/#").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                .WithTopicFilter(f => f.WithTopic("test/esp32/calculator/#").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                .Build();

            await _mqttClient.SubscribeAsync(subscribeOptions);
            _logger.LogInformation("Подписка на топики выполнена");
        }

        public bool IsConnected() => _mqttClient != null && _mqttClient.IsConnected;

        private void ProcessMqttMessage(string topic, byte[] payload)
        {
            var message = Encoding.UTF8.GetString(payload).Trim();
            _logger.LogInformation($"MQTT сообщение получено: {topic} - {message}");

            if (topic.StartsWith("test/esp32/calculator/status/"))
            {
                var mac = topic.Substring("test/esp32/calculator/status/".Length);
                var lowerMessage = message.Trim().ToLower();

                string status = "";

                if (lowerMessage.Contains("online"))
                {
                    status = "online";
                }
                else if (lowerMessage.Contains("offline"))
                {
                    status = "offline";
                }
                else
                {
                    _logger.LogWarning($"Неизвестный статус устройства: {message}");
                    return;
                }

                var device = Devices.FirstOrDefault(d => d.Name == mac);
                if (device != null)
                {
                    device.Status = status;
                    _logger.LogInformation($"Обновлено устройство: {device.Name} -> {device.Status}");
                }
                else
                {
                    Devices.Add(new Device { Name = mac, Status = status });
                    _logger.LogInformation($"Добавлено новое устройство: {mac} -> {status}");
                }

                OnMessageReceived?.Invoke();
            }
            else if (topic.StartsWith("test/esp32/calculator/"))
            {
                try
                {
                    var mac = topic.Substring("test/esp32/calculator/".Length).Split('/')[0];

                    if (message.Contains("Data: POST request received:"))
                    {
                        var dataPart = message.Split(new[] { "Data: POST request received:" }, StringSplitOptions.RemoveEmptyEntries);
                        if (dataPart.Length < 2)
                        {
                            _logger.LogWarning("Некорректный формат данных выражения");
                            return;
                        }

                        var data = dataPart[1].Trim();

                        // Разбиваем строку на пары "ключ=значение"
                        var pairs = data.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);  // обратил внимание на пробел после запятой

                        var dict = pairs.Select(p =>
                        {
                            var kv = p.Split('=');
                            var key = kv[0].Trim();
                            var value = kv[1].Trim().Replace(',', '.'); // точка для парсинга double
                            return new KeyValuePair<string, string>(key, value);
                        }).ToDictionary(x => x.Key, x => x.Value);

                        var num1 = ParseDouble(dict["num1"]);
                        var num2 = ParseDouble(dict["num2"]);
                        var operation = dict["operation"];
                        var result = ParseDouble(dict["result"]);

                        var expression = new Expression
                        {
                            Num1 = num1,
                            Num2 = num2,
                            Operator = operation,
                            Result = Math.Round(result, 2)
                        };

                        DeviceExpressions[mac] = expression;

                        _logger.LogInformation($"Обновлено выражение для устройства {mac}");

                        OnMessageReceived?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    Status = $"Ошибка обработки выражения: {ex.Message}";
                    _logger.LogError($"Ошибка обработки выражения: {ex.Message}");
                }
            }




        }

        // Метод для безопасного парсинга чисел
        private double ParseDouble(string input)
        {
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
            throw new FormatException($"Не удалось распарсить число: '{input}'");
        }




        public void Dispose()
        {
            if (!_isDisposed)
            {
                _mqttClient?.DisconnectAsync().Wait();
                _mqttClient?.Dispose();
                _isDisposed = true;
            }
        }
    }
}