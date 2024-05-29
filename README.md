# UniChat

UniChat is a Unity asset designed to enable developers to communicate directly within the Unity editor. It provides real-time chat functionality and file transfer capabilities, making it easier for team members to collaborate during development.

**_This asset is still in development_**

## Features

- **Real-Time Chat**: Enables instant communication between team members.
- **File Transfer**: Send and receive files directly within the Unity editor
- **Progress Bar**: Shows progress during file sending and receiving.
- **Customization Options**: Configure message colors.
- **Persistent Chat Log**: Automatically saves and loads chat history.

## How It Works

### Installation

**The asset depends from UniTask so go at https://github.com/Cysharp/UniTask and follow instruction**

1. Clone or download the `UniChat` repository.
2. Place the `UniChat` folder into your Unity project's `Assets` directory.

_Note: A Unity Package is available_

### Usage

1. **Opening UniChat**:
    - Go to `Tools > UniChat` in the Unity menu to open the UniChat window.

2. **Chatting**:
    - Enter your username and start typing in the chat input field.
    - Press the "Send" button or hit Enter to send your message.

3. **Sending Files**:
    - Click the "Send File" button and select the file you want to send.
    - The file transfer progress will be shown in the chat window.

4. **Customizing Settings**:
    - Click on the "Options" button at the top of the window to open the Options window.
    - Options in the Options window:
        - **User Message Color**: Choose the color for your messages.
        - **Log Message Color**: Choose the color for log messages.
        - **Chunk Size**: Select the size of file chunks for transfers.

5. **Viewing Logs**:
    - The chat log is displayed in the main window, showing the current session's messages and logs.
    - Previous chat logs are automatically loaded when the window is opened.

### Connecting Users

To use UniChat, users need to connect to each other over a network. For simplicity, you can use a VPN service like Hamachi to establish a virtual local network.

#### Using Hamachi

1. **Download and Install Hamachi**:
    - Download Hamachi from the official website and install it on each user's computer.

2. **Create a Network**:
    - One user should create a new network in Hamachi by clicking "Network" > "Create a new network".
    - Provide a network ID and password, then share these details with other users.

3. **Join the Network**:
    - Other users should join the network by clicking "Network" > "Join an existing network".
    - Enter the network ID and password provided by the network creator.

4. **Find IP Addresses**:
    - In Hamachi, each user will have a virtual IP address displayed. Use these IP addresses to connect.

5. **Connect in UniChat**:
    - **Server**:
        - One user should start the server by entering their username and clicking the "Start Server" button.
    - **Client**:
        - Other users should enter the server's Hamachi IP address, their username, and click "Connect as Client".

6. **Start Chatting**:
    - Once connected, users can start chatting and sending files as described in the usage instructions.

### Visualizations

- **Progress Bar**:
    - Shows the progress of file transfers, both for sending and receiving files.

## Known Issues

- **_This asset is still early in development_**

### Limitations

- For now it does not support when unity recompile.
