using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Serialization;
using System.Drawing;
using Console = Colorful.Console;

//This Program is an extension of the MoonShine project and is distributed as part of Project Charlemagne

/*Copyright(C) 2019-2020 <REDACTED>, All rights reserved.
This file is part of The Charlemagne project.
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
/*

//The licensor cannot revoke these freedoms as long as you follow the license terms.

namespace TcpBounce
{
    
    public class TcpForwarderSettings
    {
        public string SourcePort { get; set; }
        public string TargetIP { get; set; }
        public string TargetPort { get; set; }
    }
    public class Program
    {
        public static void Main()
        {
            Console.WriteAscii("TcpBounce 1.0.0", Color.OrangeRed);
            //we could do a For Each here but we only want to forward one port

            Program Instance = new Program();

            if (!File.Exists("Settings.xml"))
            {
                Console.WriteLine("Missing config file \"Settings.xml\"", Color.Red);
                return;
            }

            String[] Settings = File.ReadAllLines("Settings.xml");
            foreach(string Target in Settings)
            {
                String[] InstanceSettings = Target.Split(',');
                if(InstanceSettings.Length == 3)
                {
                    try
                    {
                        Instance.ForwardTCP(Convert.ToInt32(InstanceSettings[0]), InstanceSettings[1], Convert.ToInt32(InstanceSettings[2]));
                        Console.WriteLine("Now forwarding Port: " + InstanceSettings[0].ToString() + " To Target: " + InstanceSettings[1].ToString() + " On Port: " + InstanceSettings[2], Color.Yellow);
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine("Invalid config @ Line: " + InstanceSettings.ToString() + Environment.NewLine + ex.ToString(), Color.Red);
                    }
                    
                }
            }

            Console.WriteLine("Press any key to stop forwarding...", Color.Yellow);
            Console.ReadKey();

            //Instance.ForwardTCP(1984, "198.0.1.156", 1984);
        }

        private bool GetIsRunning()
        { return this.listener != null; }

        private readonly byte[] sourceBuffer = new byte[1024];
        private readonly byte[] targetBuffer = new byte[1024];

        private readonly object sync = new object();
        private TcpListener listener;
        private TcpClient sourceClient;
        private NetworkStream sourceStream;
        private TcpClient targetClient;
        private NetworkStream targetStream;

        public void ForwardTCP(int Port, string TargetIP, int TargetPort)
        {
            if (GetIsRunning())
            {
                Debug.WriteLine("Already routing...");
                return;
            }

            int sourcePort;
            IPAddress targetIP;
            int targetPort;

            try
            {
                sourcePort = int.Parse(Port.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Invalid source port: " + ex.Message);
                return;
            }
            try
            {
                targetIP = IPAddress.Parse(TargetIP);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Invalid IP address: " + ex.Message);
                return;
            }
            try
            {
                targetPort = int.Parse(TargetPort.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Invalid target port: " + ex.Message);
                return;
            }

            lock (this.sync)
            {
                try
                {
                    this.listener = new TcpListener(IPAddress.Any, sourcePort);
                    this.listener.Start();
                    this.listener.BeginAcceptTcpClient(this.ClientConnectedCallback, this.listener);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    this.Stop();
                    Debug.WriteLine("Could not begin listening: " + ex.Message);
                    return;
                }

                try
                {
                    this.targetClient = new TcpClient();
                    this.targetClient.NoDelay = true;
                    var ar = this.targetClient.BeginConnect(targetIP, targetPort, null, null);
                    var success = ar.AsyncWaitHandle.WaitOne(500);
                    if (!success)
                    {
                        throw new Exception("The connection attempt timed out.");
                    }
                    this.targetClient.EndConnect(ar);
                    this.targetStream = this.targetClient.GetStream();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    this.Stop();
                    Debug.WriteLine("Could not connect to the target end point: " + ex.Message);
                    return;
                }
                Debug.WriteLine("Connected to socket");
            }
        }

        private void ClientConnectedCallback(IAsyncResult ar)
        {
            try
            {
                lock (this.sync)
                {
                    if (!GetIsRunning())
                    {
                        return;
                    }
                    this.sourceClient = listener.EndAcceptTcpClient(ar);
                    this.sourceClient.NoDelay = true;
                    this.sourceStream = this.sourceClient.GetStream();
                    this.listener.Stop();

                    Debug.WriteLine("Connected");

                    this.ReadSourceStream();
                    this.ReadTargetStream();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                Stop();
            }
        }

        private void ReadSourceStream()
        {
            this.sourceStream.BeginRead(this.sourceBuffer, 0, this.sourceBuffer.Length, this.SourceReceivedCallback, this.sourceStream);
        }

        private void SourceReceivedCallback(IAsyncResult ar)
        {
            try
            {
                lock (this.sync)
                {
                    if (!this.GetIsRunning())
                    {
                        return;
                    }
                    var numReceived = ((NetworkStream)ar.AsyncState).EndRead(ar);
                    if (numReceived <= 0)
                    {
                        // connection closed
                        this.Stop();
                        return;
                    }
                    var sb = new StringBuilder();
                    for (int i = 0; i < numReceived; ++i)
                    {
                        sb.Append(this.sourceBuffer[i].ToString("x2"));
                        sb.Append(" ");
                    }
                    this.targetStream.Write(this.sourceBuffer, 0, numReceived);
                    Debug.WriteLine("From source: " + sb.ToString());
                    this.ReadSourceStream();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                this.Stop();
            }
        }

        private void ReadTargetStream()
        {
            this.targetStream.BeginRead(this.targetBuffer, 0, this.targetBuffer.Length, this.TargetReceivedCallback, this.targetStream);
        }

        private void TargetReceivedCallback(IAsyncResult ar)
        {
            try
            {
                lock (this.sync)
                {
                    if (!this.GetIsRunning())
                    {
                        return;
                    }
                    var numReceived = ((NetworkStream)ar.AsyncState).EndRead(ar);
                    if (numReceived <= 0)
                    {
                        // connection closed
                        this.Stop();
                        return;
                    }
                    var sb = new StringBuilder();
                    for (int i = 0; i < numReceived; ++i)
                    {
                        sb.Append(this.targetBuffer[i].ToString("x2"));
                        sb.Append(" ");
                    }
                    this.sourceStream.Write(this.targetBuffer, 0, numReceived);
                    Debug.WriteLine("From target: " + sb.ToString());
                    this.ReadTargetStream();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                this.Stop();
            }
        }

        private void SetStatusDisconnected()
        {
            Debug.WriteLine("Disconnected from socket");
            return;
        }

        private void Stop()
        {
            lock (this.sync)
            {
                if (this.sourceClient != null)
                {
                    this.sourceClient.Close();
                    this.sourceClient = null;
                }
                if (this.targetClient != null)
                {
                    this.targetClient.Close();
                    this.targetClient = null;
                }
                if (this.listener != null)
                {
                    this.listener.Stop();
                    this.listener = null;
                }

                this.SetStatusDisconnected();
            }
        }
    }
}
