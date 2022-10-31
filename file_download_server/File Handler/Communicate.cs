using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UDP_FTP.Error_Handling;
using UDP_FTP.Models;
using static UDP_FTP.Models.Enums;

namespace UDP_FTP.File_Handler
{
    class Communicate
    {
        private const string Server = "Server";
        private string Client = "Client";
        private int SessionID;
        private Socket socket;
        private IPEndPoint remoteEndpoint;
        private EndPoint remoteEP;
        private ErrorType Status;
        private byte[] buffer;
        byte[] msg;
        private string file;
        ConSettings C;


        public Communicate()
        {
            // TODO: Initializes another instance of the IPEndPoint for the remote host
            remoteEP = (EndPoint)new IPEndPoint(IPAddress.Any, 5010);

            // TODO: Specify the buffer size
            buffer = new byte[(int)Enums.Params.BUFFER_SIZE];

            // TODO: Get a random SessionID
            SessionID = new Random().Next(1000, 9999);

            // TODO: Create local IPEndpoints and a Socket to listen 
            //       Keep using port numbers and protocols mention in the assignment description
            //       Associate a socket to the IPEndpoints to start the communication
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5004));
        }

        public ErrorType StartDownload()
        {
            // TODO: Instantiate and initialize different messages needed for the communication
            // required messages are: HelloMSG, RequestMSG, DataMSG, AckMSG, CloseMSG
            // Set attribute values for each class accordingly 
            HelloMSG greetBack = new HelloMSG();
            RequestMSG req = new RequestMSG();
            DataMSG data = new DataMSG();
            AckMSG ack = new AckMSG();
            CloseMSG cls = new CloseMSG();

           var conSettings = new ConSettings()
            {
                Type = Messages.HELLO,
                To = Server,
                From = Client,
                ConID = 1,
            };
            int dataSize;
            string data;
                Console.WriteLine("\n Waiting for the next client message..");

                // Receive message
                dataSize = socket.ReceiveFrom(buffer, ref remoteEP);
                data = Encoding.ASCII.GetString(buffer, 0, dataSize);
                HelloMSG hello = JsonSerializer.Deserialize<HelloMSG>(data);
                Console.WriteLine("A message received from " + remoteEP.ToString() + " " + data);
                
                //verify message 
                  var error = ErrorHandler.VerifyGreeting(hello, conSettings);
                    if (error == 0) Console.WriteLine("No error..");
                    else throw new Exception(error.ToString());
                // Send reply message
                var helloReply = new HelloMSG()
                {
                    Type = Messages.HELLO_REPLY,
                    To = hello.From,
                    From = hello.To,
                    ConID = hello.ConID
                };
                // TODO: If no error is found then HelloMSG will be sent back

                msg = Encoding.ASCII.GetBytes(JsonSerializer.Serialize(helloReply));
                socket.SendTo(msg, msg.Length, SocketFlags.None, remoteEP);

            // TODO: Receive the next message
            // Expected message is a download RequestMSG message containing the file name
            // Receive the message and verify if there are no errors

            var rqstsize  = socket.ReceiveFrom(buffer,ref remoteEP);
            RequestMSG requestmessage = JsonSerializer.Deserialize<RequestMSG>(Encoding.ASCII.GetString(buffer,0,rqstsize));
             // TODO: Send a RequestMSG of type REPLY message to remoteEndpoint verifying the status
            RequestMSG rqst = new RequestMSG()
                {
                 Type= Messages.REPLY,
                 From= hello.To,
                 To=hello.From,
                 FileName= requestmessage.FileName,
                 ConID =requestmessage.ConID,
                 Status =  requestmessage.Status                   
                };
            // TODO: Send a RequestMSG of type REPLY message to remoteEndpoint verifying the status
            byte[] verifiedstatus = Encoding.ASCII.GetBytes(JsonSerializer.Serialize<RequestMSG>(rqst));
            socket.SendTo(verifiedstatus,verifiedstatus.Length,SocketFlags.None,remoteEP);

            // TODO:  Start sending file data by setting first the socket ReceiveTimeout value
            socket.ReceiveTimeout = 1000;

            // TODO: Open and read the text-file first
            string text = System.IO.File.ReadAllText(requestmessage.FileName);
            // Make sure to locate a path on windows and macos platforms
            string path =requestmessage.FileName;
            byte[] textdata = Encoding.ASCII.GetBytes(text);
            // TODO: Sliding window with go-back-n implementation
            int sequence = 0;
            if(textdata.Length>(int)Params.SEGMENT_SIZE){
              bool datatransfer = true;             

              while(datatransfer){                
                byte[] senddata = new byte[(int)Params.SEGMENT_SIZE];
                                 
                for(int i=0; i<(int)Params.WINDOW_SIZE; i++){   //loop for each packet                                    
                    for(int j=0; j<(int)Params.SEGMENT_SIZE; j++){ // loop to grab segment_size of bytes and put it in senddata
                    int index = sequence*(int)Params.SEGMENT_SIZE +j;
                    senddata[j] = textdata[index]; 

                    }
                      DataMSG PDU = new DataMSG(){
                        Type =Messages.DATA,
                        From = hello.To,
                        To = hello.From,
                        ConID = 1,
                        Size = senddata.Length,
                        More = (sequence%(int)Params.WINDOW_SIZE!=0),
                        Sequence = sequence,
                        Data = senddata
                        };
                        byte[] message = Encoding.ASCII.GetBytes(JsonSerializer.Serialize(PDU));
                        socket.SendTo(message,message.Length,SocketFlags.None,remoteEP);          
                }                     
                
                AckMSG[] cache = new AckMSG[(int)Params.WINDOW_SIZE];
                for(int i =0; i<=(int)Params.WINDOW_SIZE;i++){
                cache[i] =  JsonSerializer.Deserialize<AckMSG>(Encoding.ASCII.GetString(buffer,0,(socket.ReceiveFrom(buffer,SocketFlags.None,ref remoteEP))));
               // TODO: Print each confirmed sequence in the console
                Console.WriteLine(cache[i].Sequence);
                }

                // TODO: Receive and verify the acknowledgements (AckMSG) of sent messages
                 // Your client implementation should send an AckMSG message for each received DataMSG message

                if(cache[(int)Params.WINDOW_SIZE-1]==null){ //all hell breaks loose
                    int highest_sequence = 0;
                    for(int i=0; i<cache.Length; i++){
                        AckMSG current_ack = cache[i];
                        if(cache[i].Sequence>highest_sequence){
                            highest_sequence = current_ack.Sequence;
                        }
                    }
                    // receive the message and verify if there are no errors
                    if(highest_sequence!=sequence){
                        sequence =highest_sequence;
                    } else{
                        sequence++;
                    }
                }                
              }
              } else{
                //send one data packet and  a closemsg
                byte[] senddata = new byte[(int)Params.SEGMENT_SIZE];
                for(int j=0; j<((int)Params.SEGMENT_SIZE; j++){
                    int index = sequence*(int)Params.SEGMENT_SIZE +j;
                    senddata[j] = textdata[index];
              }             
                            // TODO: Send a CloseMSG message to the client for the current session
                            // Send close connection request
                            // end the loop

              DataMSG PDU = new DataMSG(){
                        Type =Messages.DATA,
                        From = hello.To,
                        To = hello.From,
                        ConID = 1,
                        Size = senddata.Length,
                        More = (sequence%(int)Params.WINDOW_SIZE!=0),
                        Sequence = sequence,
                        Data = senddata
                        };
                byte[] message = Encoding.ASCII.GetBytes(JsonSerializer.Serialize(PDU));
                socket.SendTo(message,message.Length,SocketFlags.None,remoteEP);   
                CloseMSG msg = new CloseMSG{
                    Type =Messages.CLOSE_REQUEST,
                    From =hello.To,
                    To=hello.From,
                    ConID=1
                };
                byte[] closemsg = Encoding.ASCII.GetBytes(JsonSerializer.Serialize(msg));
                socket.SendTo(closemsg,closemsg.Length,SocketFlags.None,remoteEP); 
            };            
            // TODO: Receive and verify a CloseMSG message confirmation for the current session
            // Get close connection confirmation
            // Receive the message and verify if there are no errors           

        
    }           
            
            // Calculate the length of data to be sent
            // Send file-content as DataMSG message as long as there are still values to be sent
            // Consider the WINDOW_SIZE and SEGMENT_SIZE when sending a message  
            // Make sure to address the case if remaining bytes are less than WINDOW_SIZE
            //
            // Suggestion: while there are still bytes left to send,
            // first you send a full window of data
            // second you wait for the acks
            // then you start again.
            // Console.WriteLine("Group members: {0} | {1}", "student_1", "student_2");
            // return ErrorType.NOERROR;
        }
    }
