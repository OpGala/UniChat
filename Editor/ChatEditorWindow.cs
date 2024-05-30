using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using UnityEditor;
using UnityEngine;
using Color = UnityEngine.Color;

namespace UniChat.Editor
{
    public sealed class DiscordChatWindow : EditorWindow
    {
        private DiscordSocketClient _client;
        private string _token = "";
        private ulong _channelId;
        private string _chatInput = "";
        private string _chatLog = "";
        private Vector2 _scrollPosition = Vector2.zero;

        private Color _userMessageColor = Color.green;
        private Color _logMessageColor = Color.gray;
        private bool _connected;

        private const string TokenPrefKey = "UniChat_Token";
        private const string ChannelIdPrefKey = "UniChat_ChannelId";
        private const string UserMessageColorPrefKey = "UniChat_UserMessageColor";
        private const string LogMessageColorPrefKey = "UniChat_LogMessageColor";

        [MenuItem("Tools/Discord Chat")]
        public static void ShowWindow()
        {
            GetWindow<DiscordChatWindow>("Discord Chat");
        }

        private void OnEnable()
        {
            LoadPreferences();
            InitializeDiscordClient().Forget();
        }

        private void OnDisable()
        {
            _client?.StopAsync();
        }

        private async UniTaskVoid InitializeDiscordClient()
        {
            _client = new DiscordSocketClient();

            _client.Log += LogAsync;
            _client.MessageReceived += MessageReceivedAsync;
            _client.Ready += () =>
            {
                _connected = true;
                Repaint();
                return Task.CompletedTask;
            };

            if (!string.IsNullOrEmpty(_token))
            {
                await _client.LoginAsync(TokenType.Bot, _token);
                await _client.StartAsync();
            }
        }

        private Task LogAsync(LogMessage log)
        {
            Debug.Log(log.ToString());
            return Task.CompletedTask;
        }

        private Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Channel.Id == _channelId)
            {
                _chatLog += $"<color=#{ColorUtility.ToHtmlStringRGB(_logMessageColor)}>{message.Author.Username}: {message.Content}</color>\n";
                Repaint();
            }

            return Task.CompletedTask;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField("Status: " + (_connected ? "Connected" : "Disconnected"));

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            var chatStyle = new GUIStyle(EditorStyles.textArea) { richText = true };
            EditorGUILayout.TextArea(_chatLog, chatStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            _chatInput = EditorGUILayout.TextField("Chat Input", _chatInput);

            if (GUILayout.Button("Send") || (Event.current.isKey && Event.current.keyCode == KeyCode.Return && Event.current.type == EventType.KeyUp))
            {
                SendMessageAsync().Forget();
                Event.current.Use();
            }

            if (GUILayout.Button("Options"))
            {
                OptionsWindow.ShowWindow(this);
            }

            EditorGUILayout.EndVertical();
        }

        private async UniTaskVoid SendMessageAsync()
        {
            if (!_connected || string.IsNullOrEmpty(_chatInput))
                return;

            if (_client.GetChannel(_channelId) is IMessageChannel channel)
            {
                await channel.SendMessageAsync(_chatInput);
                _chatLog += $"<color=#{ColorUtility.ToHtmlStringRGB(_userMessageColor)}>Me: {_chatInput}</color>\n";
                _chatInput = "";
                Repaint();
            }
        }

        private void SetUserMessageColor(Color color)
        {
            _userMessageColor = color;
            SavePreferences();
        }

        private void SetLogMessageColor(Color color)
        {
            _logMessageColor = color;
            SavePreferences();
        }

        private void SetToken(string token)
        {
            _token = token;
            SavePreferences();
            InitializeDiscordClient().Forget();
        }

        private void SetChannelId(ulong channelId)
        {
            _channelId = channelId;
            SavePreferences();
        }

        private void SavePreferences()
        {
            EditorPrefs.SetString(TokenPrefKey, _token);
            EditorPrefs.SetString(ChannelIdPrefKey, _channelId.ToString());
            EditorPrefs.SetString(UserMessageColorPrefKey, ColorUtility.ToHtmlStringRGB(_userMessageColor));
            EditorPrefs.SetString(LogMessageColorPrefKey, ColorUtility.ToHtmlStringRGB(_logMessageColor));
        }

        private void LoadPreferences()
        {
            if (EditorPrefs.HasKey(TokenPrefKey))
                _token = EditorPrefs.GetString(TokenPrefKey);
            if (EditorPrefs.HasKey(ChannelIdPrefKey) && ulong.TryParse(EditorPrefs.GetString(ChannelIdPrefKey), out ulong channelId))
                _channelId = channelId;
            if (EditorPrefs.HasKey(UserMessageColorPrefKey) && ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString(UserMessageColorPrefKey), out Color userMessageColor))
                _userMessageColor = userMessageColor;
            if (EditorPrefs.HasKey(LogMessageColorPrefKey) && ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString(LogMessageColorPrefKey), out Color logMessageColor))
                _logMessageColor = logMessageColor;
        }

        public sealed class OptionsWindow : EditorWindow
        {
            private DiscordChatWindow _chatEditorWindow;
            private Color _userMessageColor;
            private Color _logMessageColor;
            private string _token;
            private string _channelId;

            public static void ShowWindow(DiscordChatWindow chatEditorWindow)
            {
                var window = GetWindow<OptionsWindow>("Options");
                window._chatEditorWindow = chatEditorWindow;
                window._userMessageColor = chatEditorWindow._userMessageColor;
                window._logMessageColor = chatEditorWindow._logMessageColor;
                window._token = chatEditorWindow._token;
                window._channelId = chatEditorWindow._channelId.ToString();
                window.Show();
            }

            private void OnGUI()
            {
                _userMessageColor = EditorGUILayout.ColorField("User Message Color", _userMessageColor);
                _logMessageColor = EditorGUILayout.ColorField("Log Message Color", _logMessageColor);
                _token = EditorGUILayout.TextField("Bot Token", _token);
                _channelId = EditorGUILayout.TextField("Channel ID", _channelId);

                if (GUILayout.Button("Apply"))
                {
                    _chatEditorWindow.SetUserMessageColor(_userMessageColor);
                    _chatEditorWindow.SetLogMessageColor(_logMessageColor);
                    _chatEditorWindow.SetToken(_token);
                    if (ulong.TryParse(_channelId, out ulong channelId))
                    {
                        _chatEditorWindow.SetChannelId(channelId);
                    }
                    Close();
                }
            }
        }
    }
}
