﻿// SPDX-FileCopyrightText: 2024 varelen
//
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Varelen.Mimoria.Core;
using Varelen.Mimoria.Core.Buffer;

namespace Varelen.Mimoria.Server.Network;

public abstract class AsyncTcpSocketServer : ISocketServer
{
    private readonly ILogger<AsyncTcpSocketServer> logger;
    private readonly Socket socket;
    private readonly ConcurrentDictionary<ulong, TcpConnection> connections;
    private ulong connectionIdCounter;

    public ulong Connections => (ulong)this.connections.Count;

    protected AsyncTcpSocketServer(ILogger<AsyncTcpSocketServer> logger)
    {
        this.logger = logger;
        // TODO: Keep alive
        this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        this.connections = [];
        // TODO: Dual mode
    }

    public void Start(string ip, ushort port, ushort backlog = 50)
    {
        this.socket.Bind(new IPEndPoint(IPAddress.Parse(ip), port));
        this.socket.Listen(backlog);

        _ = this.AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (this.socket.IsBound)
            {
                Socket clientSocket = await this.socket.AcceptAsync();
                clientSocket.NoDelay = true;

                ulong connectionId = Interlocked.Increment(ref this.connectionIdCounter);
                var tcpConnection = new TcpConnection(connectionId, this, clientSocket, clientSocket.RemoteEndPoint!);

                bool added = this.connections.TryAdd(connectionId, tcpConnection);
                Debug.Assert(added, $"Unable to add new connection with id '{connectionId}'");

                this.HandleOpenConnection(tcpConnection);

                _ = this.ReceiveAsync(tcpConnection);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // Ignore
        }
    }

    private async Task ReceiveAsync(TcpConnection tcpConnection)
    {
        try
        {
            while (tcpConnection.Connected)
            {
                int received = await tcpConnection.Socket.ReceiveAsync(tcpConnection.ReceiveBuffer.AsMemory(), SocketFlags.None);
                if (received == 0)
                {
                    tcpConnection.Disconnect();
                    return;
                }

                if (received < ProtocolDefaults.MinPacketLength)
                {
                    tcpConnection.Disconnect();
                    this.logger.LogWarning("Received smaller packet length '{PacketLength}' than allowed min packet length '{MinPacketLength}' from '{RemoteAddress}'", received, ProtocolDefaults.MinPacketLength, tcpConnection.RemoteEndPoint);
                    return;
                }

                tcpConnection.ExpectedPacketLength = BinaryPrimitives.ReadInt32BigEndian(tcpConnection.ReceiveBuffer);
                tcpConnection.ReceivedBytes = received - 4;
                tcpConnection.ByteBuffer.WriteBytes(tcpConnection.ReceiveBuffer.AsSpan(4, received - 4));

                while (tcpConnection.ReceivedBytes < tcpConnection.ExpectedPacketLength)
                {
                    int bytesToReceive = Math.Min(tcpConnection.ExpectedPacketLength - tcpConnection.ReceivedBytes, tcpConnection.ReceiveBuffer.Length);
                    
                    received = await tcpConnection.Socket.ReceiveAsync(tcpConnection.ReceiveBuffer.AsMemory(0, bytesToReceive), SocketFlags.None);
                    if (received == 0)
                    {
                        tcpConnection.Disconnect();
                        return;
                    }

                    tcpConnection.ReceivedBytes += received;
                    tcpConnection.ByteBuffer.WriteBytes(tcpConnection.ReceiveBuffer.AsSpan(0, received));
                }

                IByteBuffer byteBuffer = PooledByteBuffer.FromPool();
                byteBuffer.WriteBytes(tcpConnection.ByteBuffer.Bytes.AsSpan(0, tcpConnection.ExpectedPacketLength));

                await this.HandlePacketReceived(tcpConnection, byteBuffer);

                tcpConnection.ByteBuffer.Clear();
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            tcpConnection.Disconnect();
        }
        catch (Exception)
        {
            tcpConnection.Disconnect();

            // TODO: What to do? If we ignore other exceptions then they are silently dropped
            // because we are not awaiting this method
        }
    }

    protected abstract ValueTask HandlePacketReceived(TcpConnection tcpConnection, IByteBuffer byteBuffer);

    protected abstract void HandleOpenConnection(TcpConnection tcpConnection);
    protected abstract void HandleCloseConnection(TcpConnection tcpConnection);

    internal void HandleCloseConnectionInternal(TcpConnection tcpConnection)
    {
        bool removed = this.connections.TryRemove(tcpConnection.Id, out _);
        Debug.Assert(removed, $"Unable to remove connection with id '{tcpConnection.Id}'");
        
        this.HandleCloseConnection(tcpConnection);
    }

    public void Stop()
    {
        try
        {
            this.socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // Ignored
        }
        finally
        {
            this.socket.Close();
        }

        foreach (var (_, tcpConnection) in this.connections)
        {
            tcpConnection.Disconnect();
        }
    }
}
