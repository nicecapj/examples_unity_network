﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Runtime.CompilerServices;

using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Protocol;
using SuperSocket.SocketEngine;



//TODO 1. 주기적으로 접속한 세션이 패킷을 주고 받았는지 조사(좀비 클라이언트 검사)

namespace LobbyServer
{
    public class GameServer : AppServer<ClientSession, EFBinaryRequestInfo>
    {
        public static ServerOption ServerOption;
        public static SuperSocket.SocketBase.Logging.ILog MainLogger;

        SuperSocket.SocketBase.Config.IServerConfig m_Config;

        PacketProcessor MainPacketProcessor = new PacketProcessor();
        RoomManager LobbyMgr = new RoomManager();

        
        public GameServer()
            : base(new DefaultReceiveFilterFactory<ReceiveFilter, EFBinaryRequestInfo>())
        {
            NewSessionConnected += new SessionHandler<ClientSession>(OnConnected);
            SessionClosed += new SessionHandler<ClientSession, CloseReason>(OnClosed);
            NewRequestReceived += new RequestHandler<ClientSession, EFBinaryRequestInfo>(OnPacketReceived);
        }

        public void InitConfig(ServerOption option)
        {
            ServerOption = option;

            m_Config = new SuperSocket.SocketBase.Config.ServerConfig
            {
                Name = option.Name,
                Ip = "Any",
                Port = option.Port,
                Mode = SocketMode.Tcp,
                MaxConnectionNumber = option.MaxConnectionNumber,
                MaxRequestLength = option.MaxRequestLength,
                ReceiveBufferSize = option.ReceiveBufferSize,
                SendBufferSize = option.SendBufferSize
            };
        }

        public void CreateStartServer()
        {
            try
            {
                bool bResult = Setup(new SuperSocket.SocketBase.Config.RootConfig(), m_Config, logFactory: new SuperSocket.SocketBase.Logging.NLogLogFactory());

                if (bResult == false)
                {
                    Console.WriteLine("[ERROR] 서버 네트워크 설정 실패 ㅠㅠ");
                    return;
                }
                else
                {
                    MainLogger = base.Logger;
                    MainLogger.Info("서버 초기화 성공");
                }


                CreateComponent();

                Start();

                MainLogger.Info("서버 생성 성공");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 서버 생성 실패: {ex.ToString()}");
            }          
        }

        
        public void StopServer()
        {            
            Stop();

            MainPacketProcessor.Destory();
        }

        public ERROR_CODE CreateComponent()
        {
            Room.NetSendFunc = this.SendData;
            LobbyMgr.CreateRooms();

            MainPacketProcessor = new PacketProcessor();
            MainPacketProcessor.CreateAndStart(LobbyMgr.GetRoomList(), this);
            
            MainLogger.Info("CreateComponent - Success");
            return ERROR_CODE.NONE;
        }
                       

        public bool SendData(string sessionID, UInt16 packetID, byte[] bodyData)
        {
            var session = GetSessionByID(sessionID);

            try
            {
                if (session == null)
                {
                    return false;
                }

                Int16 bodyDataSize = 0;
                if (bodyData != null)
                {
                    bodyDataSize = (Int16)bodyData.Length;
                }
                var packetSize = bodyDataSize + PacketDef.PACKET_HEADER_SIZE;

                List<byte> dataSource = new List<byte>();
                dataSource.AddRange(BitConverter.GetBytes((UInt16)packetSize));
                dataSource.AddRange(BitConverter.GetBytes((UInt16)packetID));
                dataSource.AddRange(new byte[] { (byte)0 });

                if (bodyData != null)
                {
                    dataSource.AddRange(bodyData);
                }

                var sendPacket = dataSource.ToArray();
                session.Send(sendPacket, 0, sendPacket.Length);
            }
            catch(Exception ex)
            {
                // TimeoutException 예외가 발생할 수 있다
                GameServer.MainLogger.Error($"{ex.ToString()},  {ex.StackTrace}");

                session.SendEndWhenSendingTimeOut(); 
                session.Close();
            }
            return true;
        }

        public void Distribute(ServerPacketData requestPacket)
        {
            MainPacketProcessor.InsertPacket(requestPacket);
        }
                        
        void OnConnected(ClientSession session)
        {
            MainLogger.Info(string.Format("세션 번호 {0} 접속", session.SessionID));
                        
            var packet = ServerPacketData.MakeNTFInConnectOrDisConnectClientPacket(true, session.SessionID);            
            Distribute(packet);
        }

        void OnClosed(ClientSession session, CloseReason reason)
        {
            MainLogger.Info(string.Format("세션 번호 {0} 접속해제: {1}", session.SessionID, reason.ToString()));
            
            var packet = ServerPacketData.MakeNTFInConnectOrDisConnectClientPacket(false, session.SessionID);
            Distribute(packet);
        }

        void OnPacketReceived(ClientSession session, EFBinaryRequestInfo reqInfo)
        {
            MainLogger.Debug(string.Format("세션 번호 {0} 받은 데이터 크기: {1}, ThreadId: {2}", session.SessionID, reqInfo.Body.Length, System.Threading.Thread.CurrentThread.ManagedThreadId));

            var packet = new ServerPacketData();
            packet.SessionID = session.SessionID;
            packet.PacketSize = reqInfo.Size;            
            packet.PacketID = reqInfo.PacketID;
            packet.Type = reqInfo.Type;
            packet.BodyData = reqInfo.Body;
                    
            Distribute(packet);
        }
    }

    
}
