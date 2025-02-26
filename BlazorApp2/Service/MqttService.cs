using MQTTnet;
using MQTTnet.Protocol;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace BlazorApp2.Service
{
    public class Device
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "offline";
    }

    public class Expression
    {
        public int Num1 { get; set; }
        public string Operator { get; set; } = "";
        public int Num2 { get; set; }
        public int Result { get; set; }
    }

    public class MqttService : IDisposable
    {
        private readonly ILogger<MqttService> _logger;
        private IMqttClient _mqttClient;
        private bool _isConnected = false;
        private bool _isDisposed = false; // Флаг для отслеживания состояния

        public string Status { get; private set; } = "Отключено";

        public event Action OnMessageReceived;

        public List<Device> Devices { get; private set; } = new()
        {
            new Device { Name = "ESP32-1", Status = "offline" },
            new Device { Name = "ESP32-2", Status = "offline" },
            new Device { Name = "ESP32-3", Status = "offline" }
        };

        public Expression Expression { get; private set; } = new();

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

                for (int i = 0; i < 5; i++) // 5 попыток переподключения
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5)); // Подождать перед повторной попыткой
                        _logger.LogInformation($"Попытка переподключения {i + 1}...");

                        // Если клиент был освобожден, создаем новый
                        if (_mqttClient == null || _isDisposed)
                        {
                            _logger.LogInformation("Создание нового MQTT клиента...");
                            _mqttClient = new MqttClientFactory().CreateMqttClient();
                            await InitializeMqttClient(); // Повторная инициализация
                            _isDisposed = false; // Сбрасываем флаг
                        }

                        await _mqttClient.ConnectAsync(options);
                        return;
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
                _logger.LogInformation("Попытка подключения завершена");
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
                .WithTopicFilter(f => f.WithTopic("devices/status").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                .WithTopicFilter(f => f.WithTopic("expression/data").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                .Build();

            await _mqttClient.SubscribeAsync(subscribeOptions);
        }

        public bool IsConnected() => _mqttClient != null && _mqttClient.IsConnected;

        private void ProcessMqttMessage(string topic, byte[] payload)
        {
            var message = Encoding.UTF8.GetString(payload);

            if (topic == "devices/status")
            {
                try
                {
                    var deviceStatusList = JsonSerializer.Deserialize<List<Device>>(message);
                    if (deviceStatusList != null)
                    {
                        Devices = deviceStatusList;
                        _logger.LogInformation("Обновлен список устройств");
                    }
                }
                catch (Exception ex)
                {
                    Status = $"Ошибка обработки данных устройств: {ex.Message}";
                    _logger.LogError($"Ошибка обработки данных устройств: {ex.Message}");
                }
            }
            else if (topic == "expression/data")
            {
                try
                {
                    var expression = JsonSerializer.Deserialize<Expression>(message);
                    if (expression != null)
                    {
                        Expression = expression;
                        _logger.LogInformation("Обновлены данные выражения");
                    }
                }
                catch (Exception ex)
                {
                    Status = $"Ошибка обработки выражения: {ex.Message}";
                    _logger.LogError($"Ошибка обработки выражения: {ex.Message}");
                }
            }
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