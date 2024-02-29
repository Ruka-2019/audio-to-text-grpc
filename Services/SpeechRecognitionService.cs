using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.CognitiveServices.Speech;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech.Audio;
using SpeechRecognitionGrpcService;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Services
{
    public class SpeechRecognitionService : SpeechRecognitionServiceInterface.SpeechRecognitionServiceInterfaceBase
    {
        private readonly ILogger<SpeechRecognitionService> _logger;
        private readonly ConcurrentDictionary<string, RecognitionSession> _recognizers = new();

        public SpeechRecognitionService(ILogger<SpeechRecognitionService> logger)
        {
            _logger = logger;
        }

        public override async Task CreateService(CreateServiceRequest request,
            IServerStreamWriter<RecognitionResponse> responseStream, ServerCallContext context)
        {
            var id = Guid.NewGuid().ToString();
            await responseStream.WriteAsync(new RecognitionResponse { Text = id, Type = "SessionId" });

            // Set up config from config.json
            string configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\"));
            var builder = new ConfigurationBuilder()
                .SetBasePath(configPath)
                .AddJsonFile("config.json", optional: true, reloadOnChange: true);

            var Configuration = builder.Build();

            var subscriptionKey = Configuration["AzureSpeechService:SubscriptionKey"];
            var serviceRegion = Configuration["AzureSpeechService:ServiceRegion"];
            var saveWav = Configuration["SaveWav"];
            // var speechRecognitionLanguage = Configuration["CaptureLanguage"];


            string projectDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string outputPath = Path.Combine(projectDirectory, "captured_audio.wav");
            bool saveWavFlag = saveWav != null && saveWav == "True";

            var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, serviceRegion);
            speechConfig.SpeechRecognitionLanguage = request.Language;

            var pushStream = AudioInputStream.CreatePushStream();
            var audioConfig = AudioConfig.FromStreamInput(pushStream);
            var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            var capture = new WasapiLoopbackCapture();

            var session = new RecognitionSession
            {
                Recognizer = recognizer,
                ExpirationCts = new CancellationTokenSource(),
                Capture = capture
            };

            _recognizers.TryAdd(id, session);

            recognizer.Recognizing += async (s, e) =>
            {
                _logger.LogInformation("Recognizing: {Text}", e.Result.Text);
                try
                {
                    await responseStream.WriteAsync(new RecognitionResponse
                        { Text = e.Result.Text, Type = "Recognizing" });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Recognizing Error: Error writing to response stream.");
                    // Handle or rethrow the exception as appropriate for your application
                }
            };

            recognizer.Recognized += async (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    _logger.LogInformation("Recognized: {Text}", e.Result.Text);
                    try
                    {
                        await responseStream.WriteAsync(new RecognitionResponse
                            { Text = e.Result.Text, Type = "Recognized" });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Recognized Error: Error writing to response stream.");
                        // Handle or rethrow the exception as appropriate for your application
                    }
                }
            };

            recognizer.Canceled += (s, e) =>
            {
                _logger.LogWarning("Recognition canceled. Reason: {Reason}", e.Reason);
            };

            recognizer.SessionStopped += (s, e) =>
            {
                _logger.LogInformation("Recognition session stopped.");
            };

            // Additional setup and start recognition
            await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

            _logger.LogInformation(capture.WaveFormat.ToString());
            WaveFileWriter writer = null;
            capture.DataAvailable += (sender, args) =>
            {
                if (args.BytesRecorded > 0)
                {
                    byte[] buffer = new byte[args.BytesRecorded];
                    Array.Copy(args.Buffer, buffer, args.BytesRecorded);
                    byte[] convertedAudio = ConvertAudioData(buffer, args.BytesRecorded, capture.WaveFormat);
                    pushStream.Write(convertedAudio);
                    if (!saveWavFlag) return; // If saveFlag is 
                    if (writer == null)
                    {
                        writer = new WaveFileWriter(outputPath, capture.WaveFormat);
                    }

                    writer.Write(buffer, 0, args.BytesRecorded);
                }
            };

            capture.RecordingStopped += (sender, args) =>
            {
                pushStream.Close();
                writer?.Dispose();
                capture.Dispose();
            };

            // Start capturing
            capture.StartRecording();

            _logger.LogInformation("Capturing audio for speech recognition. stop after 1 mins ...");
            var expirationTask = Task.Delay(TimeSpan.FromMinutes(1), session.ExpirationCts.Token)
                .ContinueWith(async t =>
                {
                    if (!t.IsCanceled)
                    {
                        _logger.LogInformation("Session expired for sessionId: {request.Id}", id);
                        await StopRecognitionSession(id);
                    }
                }, TaskScheduler.Default);
            _logger.LogInformation("Recording...");
            while (_recognizers.ContainsKey(id) && !context.CancellationToken.IsCancellationRequested)
            {
                // Wait for 5 seconds before the next check
                await Task.Delay(5000);
            }
        }

        private async Task StopRecognitionSession(string id)
        {
            if (_recognizers.TryRemove(id, out var session))
            {
                await session.Recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                session.Capture.StopRecording();
                session.ExpirationCts.Dispose();
            }
        }

        public override async Task<Empty> DeleteService(SpeechRecognitionGrpcService.DeleteServiceRequest request,
            ServerCallContext context)
        {
            _logger.LogInformation("Delete request for session {request.Id}", request.Id);
            await StopRecognitionSession(request.Id);
            return await Task.FromResult(new Empty());
        }

        public override Task<Empty> KeepServiceAlive(SpeechRecognitionGrpcService.KeepServiceAliveRequest request,
            ServerCallContext context)
        {
            if (_recognizers.TryGetValue(request.Id, out var session))
            {
                _logger.LogInformation("Keep Alive for session {request.Id}", request.Id);
                // Cancel the current expiration task
                session.ExpirationCts.Cancel();

                // Dispose the old CancellationTokenSource and create a new one
                session.ExpirationCts.Dispose();
                session.ExpirationCts = new CancellationTokenSource();

                // Schedule a new expiration task
                var expirationTask = Task.Delay(TimeSpan.FromMinutes(1), session.ExpirationCts.Token)
                    .ContinueWith(async t =>
                    {
                        _logger.LogInformation("Session expired for sessionId: {request.Id}", request.Id);
                        if (!t.IsCanceled)
                        {
                            await StopRecognitionSession(request.Id);
                        }
                    }, TaskScheduler.Default);
            }

            return Task.FromResult(new Empty());
        }


        public byte[] ConvertAudioData(byte[] buffer, int bytesRecorded, WaveFormat originalFormat)
        {
            using (var memStream = new MemoryStream(buffer))
            using (var waveStream = new RawSourceWaveStream(memStream, originalFormat))
            using (var resampler = new MediaFoundationResampler(waveStream, new WaveFormat(16000, 16, 1)))
            {
                // MediaFoundationResampler automatically handles conversion and resampling
                resampler.ResamplerQuality = 60; // Set the quality of the resampling

                // Read resampled audio into a byte array and return
                using (var resultStream = new MemoryStream())
                {
                    byte[] resampleBuffer = new byte[4096];
                    int bytesRead;
                    while ((bytesRead = resampler.Read(resampleBuffer, 0, resampleBuffer.Length)) > 0)
                    {
                        resultStream.Write(resampleBuffer, 0, bytesRead);
                    }

                    return resultStream.ToArray();
                }
            }
        }
    }

    public class RecognitionSession
    {
        public SpeechRecognizer Recognizer { get; set; }
        public CancellationTokenSource ExpirationCts { get; set; }

        public WasapiLoopbackCapture Capture { get; set; }
    }
}