using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UniChat.Editor
{
    public sealed class ChatEditorWindow : EditorWindow
    {
        private const string TempChatLogFilePath = "Temp/UniChat_ChatLog.txt";

        private string _chatInput = "";
        private string _chatLog = "";
        private Vector2 _scrollPosition = Vector2.zero;
        private TcpClient _client;
        private NetworkStream _networkStream;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _connected;
        private string _ipAddress = "IP Address";
        #pragma warning disable 414
        private bool _isServer = true;
        #pragma warning restore 414
        private TcpListener _listener;
        private string _username = "Username";

        // Colors for chat messages
        private Color _userMessageColor = Color.black;
        private Color _logMessageColor = Color.blue;
        private int _chunkSize = 65536; // Default chunk size (64KB)

        [MenuItem("Window/UniChat")]
        public static void ShowWindow()
        {
            GetWindow<ChatEditorWindow>("UniChat");
        }

        private void OnEnable()
        {
            LoadChatLog();
        }

        private void OnDisable()
        {
            SaveChatLog();
            Disconnect();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField("Status: " + (_connected ? "Connected" : "Disconnected"));

            _username = EditorGUILayout.TextField("Username", _username);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            GUIStyle chatStyle = new GUIStyle(EditorStyles.textArea) { richText = true };
            EditorGUILayout.TextArea(_chatLog, chatStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            _chatInput = EditorGUILayout.TextField("Chat Input", _chatInput);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Send"))
            {
                SendMessageAsync().Forget();
            }

            if (GUILayout.Button("Send File"))
            {
                SendFileAsync().Forget();
            }

            if (!_connected)
            {
                _ipAddress = EditorGUILayout.TextField("IP Address", _ipAddress);
                if (GUILayout.Button("Connect as Client"))
                {
                    _isServer = false;
                    ConnectToServerAsync(_ipAddress).Forget();
                }
                if (GUILayout.Button("Start Server"))
                {
                    _isServer = true;
                    StartServerAsync().Forget();
                }
            }

            if (GUILayout.Button("Show My IP"))
            {
                ShowLocalIPAddress();
            }

            if (GUILayout.Button("Clear Chat"))
            {
                ClearChat();
            }

            if (GUILayout.Button("Options"))
            {
                OptionsWindow.ShowWindow(this);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private async UniTaskVoid StartServerAsync()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, 5000);
                _listener.Start();
                LogMessage("Server started...");

                _client = await _listener.AcceptTcpClientAsync();
                _networkStream = _client.GetStream();
                _connected = true;

                LogMessage("Client connected...");
                _cancellationTokenSource = new CancellationTokenSource();
                ReceiveMessagesAsync(_cancellationTokenSource.Token).Forget();
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error starting server: " + ex.Message);
                Disconnect();
            }
        }

        private async UniTaskVoid ConnectToServerAsync(string ipAddress)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ipAddress, 5000);
                _networkStream = _client.GetStream();
                _connected = true;

                LogMessage("Connected to server...");
                _cancellationTokenSource = new CancellationTokenSource();
                ReceiveMessagesAsync(_cancellationTokenSource.Token).Forget();
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error connecting to server: " + ex.Message);
                Disconnect();
            }
        }

        private async UniTaskVoid SendMessageAsync()
        {
            if (!_connected || string.IsNullOrEmpty(_chatInput))
                return;

            try
            {
                string message = $"{_username}: {_chatInput}";
                byte[] data = Encoding.UTF8.GetBytes("MSG:" + message);
                await _networkStream.WriteAsync(data, 0, data.Length);
                AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_userMessageColor)}>Me: {_chatInput}</color>");
                _chatInput = "";
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error sending message: " + ex.Message);
                Disconnect();
            }
        }

        private async UniTaskVoid SendFileAsync()
        {
            if (!_connected)
                return;

            string filePath = EditorUtility.OpenFilePanel("Select file to send", "", "");
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(filePath);
                string fileName = Path.GetFileName(filePath);
                int totalChunks = (fileData.Length + _chunkSize - 1) / _chunkSize;

                for (int i = 0; i < totalChunks; i++)
                {
                    int currentChunkSize = Math.Min(_chunkSize, fileData.Length - i * _chunkSize);
                    byte[] chunkData = new byte[currentChunkSize];
                    Buffer.BlockCopy(fileData, i * _chunkSize, chunkData, 0, currentChunkSize);
                    byte[] messageData = Encoding.UTF8.GetBytes($"FILE:{fileName}:{i}:{totalChunks}:");
                    byte[] data = new byte[messageData.Length + chunkData.Length];
                    Buffer.BlockCopy(messageData, 0, data, 0, messageData.Length);
                    Buffer.BlockCopy(chunkData, 0, data, messageData.Length, chunkData.Length);

                    await _networkStream.WriteAsync(data, 0, data.Length);

                    // Update progress
                    EditorUtility.DisplayProgressBar("File Transfer", $"Sending {fileName} ({i + 1}/{totalChunks})", (float)(i + 1) / totalChunks);
                }
                AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_userMessageColor)}>Me: Sent file {fileName}</color>");
                EditorUtility.ClearProgressBar();
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error sending file: " + ex.Message);
                Disconnect();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }


        private async UniTaskVoid ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[4096];
                string currentFileName = null;
                MemoryStream fileStream = null;
                int totalChunks = 0;

                while (_connected)
                {
                    int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead > 0)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        if (message.StartsWith("MSG:"))
                        {
                            message = message.Substring(4); // Remove "MSG:"
                            AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_userMessageColor)}>{message}</color>");
                        }
                        else if (message.StartsWith("FILE:"))
                        {
                            string[] fileInfo = message.Split(':');
                            string fileName = fileInfo[1];
                            int chunkIndex = int.Parse(fileInfo[2]);
                            totalChunks = int.Parse(fileInfo[3]);

                            int fileDataIndex = message.IndexOf(':', 5) + 1;
                            byte[] fileData = new byte[bytesRead - fileDataIndex];
                            Buffer.BlockCopy(buffer, fileDataIndex, fileData, 0, fileData.Length);

                            if (chunkIndex == 0) // Start of file transfer
                            {
                                if (EditorUtility.DisplayDialog("File Transfer", $"Do you want to receive the file '{fileName}'?", "Yes", "No"))
                                {
                                    string savePath = EditorUtility.SaveFilePanel("Save File", "", fileName, Path.GetExtension(fileName));
                                    if (!string.IsNullOrEmpty(savePath))
                                    {
                                        fileStream = new MemoryStream();
                                        currentFileName = savePath;
                                        AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_logMessageColor)}>Receiving file: {fileName}</color>");
                                    }
                                    else
                                    {
                                        AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_logMessageColor)}>File transfer '{fileName}' cancelled by user.</color>");
                                        continue;
                                    }
                                }
                                else
                                {
                                    AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_logMessageColor)}>File transfer '{fileName}' declined.</color>");
                                    continue;
                                }
                            }

                            fileStream.Write(fileData, 0, fileData.Length);
                            EditorUtility.DisplayProgressBar("File Transfer", $"Receiving {fileName} ({chunkIndex + 1}/{totalChunks})", (float)(chunkIndex + 1) / totalChunks);
                            AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_logMessageColor)}>Received chunk {chunkIndex + 1}/{totalChunks} for {fileName}</color>");

                            if (chunkIndex == totalChunks - 1) // End of file transfer
                            {
                                File.WriteAllBytes(currentFileName, fileStream.ToArray());
                                AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_logMessageColor)}>File {fileName} saved to {currentFileName}</color>");
                                fileStream.Close();
                                fileStream = null;
                                currentFileName = null;
                                EditorUtility.ClearProgressBar();
                            }
                        }
                        Repaint(); // Redraw the window
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Error receiving messages: " + ex.Message);
                Disconnect();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }


        private void Disconnect()
        {
            try
            {
                _connected = false;
                _cancellationTokenSource?.Cancel();
                _networkStream?.Close();
                _client?.Close();
                _listener?.Stop();
                AppendMessage("Disconnected...");
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error during disconnect: " + ex.Message);
            }
        }

        private static void ShowLocalIPAddress()
        {
            string localIP = "Not available";
            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        localIP = ip.ToString();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Error getting local IP: " + ex.Message);
            }

            EditorUtility.DisplayDialog("Local IP Address", "Your IP Address: " + localIP, "OK");
        }

        private void SaveChatLog()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TempChatLogFilePath) ?? string.Empty);
                File.WriteAllText(TempChatLogFilePath, _chatLog);
            }
            catch (Exception ex)
            {
                Debug.LogError("Error saving chat log: " + ex.Message);
            }
        }

        private void LoadChatLog()
        {
            try
            {
                if (File.Exists(TempChatLogFilePath))
                {
                    _chatLog = File.ReadAllText(TempChatLogFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Error loading chat log: " + ex.Message);
            }
        }

        private void ClearChat()
        {
            _chatLog = "";
        }

        private void AppendMessage(string message)
        {
            _chatLog += message + "\n";
        }

        private void LogMessage(string message)
        {
            AppendMessage($"<color=#{ColorUtility.ToHtmlStringRGB(_logMessageColor)}>{message}</color>");
        }

        private void SetUserMessageColor(Color color)
        {
            _userMessageColor = color;
        }

        private void SetLogMessageColor(Color color)
        {
            _logMessageColor = color;
        }

        private void SetChunkSize(int size)
        {
            _chunkSize = size;
        }

        public sealed class OptionsWindow : EditorWindow
        {
            private ChatEditorWindow _chatEditorWindow;
            private Color _userMessageColor;
            private Color _logMessageColor;
            private int _chunkSize;
            private readonly string[] _chunkSizeOptions = new[] { "8KB", "64KB", "256KB" };
            private int _selectedChunkSizeOption;

            public static void ShowWindow(ChatEditorWindow chatEditorWindow)
            {
                var window = GetWindow<OptionsWindow>("Options");
                window._chatEditorWindow = chatEditorWindow;
                window._userMessageColor = chatEditorWindow._userMessageColor;
                window._logMessageColor = chatEditorWindow._logMessageColor;
                window._chunkSize = chatEditorWindow._chunkSize;
                window._selectedChunkSizeOption = Array.IndexOf(window._chunkSizeOptions, $"{window._chunkSize / 1024}KB");
                window.Show();
            }

            private void OnGUI()
            {
                _userMessageColor = EditorGUILayout.ColorField("User Message Color", _userMessageColor);
                _logMessageColor = EditorGUILayout.ColorField("Log Message Color", _logMessageColor);

                _selectedChunkSizeOption = EditorGUILayout.Popup("Chunk Size", _selectedChunkSizeOption, _chunkSizeOptions);
                _chunkSize = int.Parse(_chunkSizeOptions[_selectedChunkSizeOption].Replace("KB", "")) * 1024;

                if (GUILayout.Button("Apply"))
                {
                    _chatEditorWindow.SetUserMessageColor(_userMessageColor);
                    _chatEditorWindow.SetLogMessageColor(_logMessageColor);
                    _chatEditorWindow.SetChunkSize(_chunkSize);
                    Close();
                }
            }
        }
    }

    public static class UniTaskExtensions
    {
        public static void Forget(this UniTask task) { }
    }
}
