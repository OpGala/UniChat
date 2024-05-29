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
            EditorGUILayout.TextArea(_chatLog, GUILayout.ExpandHeight(true));
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

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private async UniTaskVoid StartServerAsync()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, 5000);
                _listener.Start();
                _chatLog += "Server started...\n";

                _client = await _listener.AcceptTcpClientAsync();
                _networkStream = _client.GetStream();
                _connected = true;

                _chatLog += "Client connected...\n";
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

                _chatLog += "Connected to server...\n";
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
                _chatLog += "Me: " + _chatInput + "\n";
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
                const int chunkSize = 1024; // 1KB chunks
                int totalChunks = (fileData.Length + chunkSize - 1) / chunkSize;
                
                for (int i = 0; i < totalChunks; i++)
                {
                    int currentChunkSize = Math.Min(chunkSize, fileData.Length - i * chunkSize);
                    byte[] chunkData = new byte[currentChunkSize];
                    Buffer.BlockCopy(fileData, i * chunkSize, chunkData, 0, currentChunkSize);
                    byte[] messageData = Encoding.UTF8.GetBytes($"FILE:{fileName}:{i}:{totalChunks}:");
                    byte[] data = new byte[messageData.Length + chunkData.Length];
                    Buffer.BlockCopy(messageData, 0, data, 0, messageData.Length);
                    Buffer.BlockCopy(chunkData, 0, data, messageData.Length, chunkData.Length);

                    await _networkStream.WriteAsync(data, 0, data.Length);
                }
                _chatLog += "Me: Sent file " + fileName + "\n";
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error sending file: " + ex.Message);
                Disconnect();
            }
        }

        private async UniTaskVoid ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[4096];
                while (_connected)
                {
                    int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead > 0)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        if (message.StartsWith("MSG:"))
                        {
                            message = message.Substring(4); // Remove "MSG:"
                            _chatLog += message + "\n";
                        }
                        else if (message.StartsWith("FILE:"))
                        {
                            string[] fileInfo = message.Split(':');
                            string fileName = fileInfo[1];
                            int chunkIndex = int.Parse(fileInfo[2]);
                            int totalChunks = int.Parse(fileInfo[3]);

                            byte[] fileData = new byte[bytesRead - message.IndexOf(':', 5) - 1];
                            Buffer.BlockCopy(buffer, message.IndexOf(':', 5) + 1, fileData, 0, fileData.Length);

                            if (EditorUtility.DisplayDialog("File Transfer", $"Do you want to receive the file '{fileName}'?", "Yes", "No"))
                            {
                                string savePath = EditorUtility.SaveFilePanel("Save File", "", fileName, "");
                                if (!string.IsNullOrEmpty(savePath))
                                {
                                    await using (var fs = new FileStream(savePath, FileMode.Append, FileAccess.Write))
                                    {
                                        await fs.WriteAsync(fileData, 0, fileData.Length, cancellationToken);
                                    }
                                    _chatLog += $"Received file chunk {chunkIndex + 1}/{totalChunks} for {fileName}\n";
                                }
                            }
                            else
                            {
                                _chatLog += $"File transfer '{fileName}' declined.\n";
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
                _chatLog += "Disconnected...\n";
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError("Error during disconnect: " + ex.Message);
            }
        }

        private void ShowLocalIPAddress()
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
    }

    public static class UniTaskExtensions
    {
        public static void Forget(this UniTask task) { }
    }
}