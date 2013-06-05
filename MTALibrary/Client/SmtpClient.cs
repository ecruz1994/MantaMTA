﻿using System;
using System.Net.Mail;
using System.Net.Sockets;
using System.Threading;
using Colony101.MTA.Library.Client.BO;

namespace Colony101.MTA.Library.Client
{
	public class SmtpClient
	{
		private static Thread _ClientThread = null;

		/// <summary>
		/// Enqueue a message for delivery.
		/// </summary>
		/// <param name="outboundIP">The IP address that should be used to relay this message.</param>
		/// <param name="mailFrom"></param>
		/// <param name="rcptTo"></param>
		/// <param name="message"></param>
		public static void Enqueue(string outboundIP, string mailFrom, string[] rcptTo, string message)
		{
			MtaMessage msg = MtaMessage.Create(outboundIP, mailFrom, rcptTo, message);
			msg.Queue();
			Start();
		}

		/// <summary>
		/// Starts the SMTP Client.
		/// </summary>
		public static void Start()
		{
			if (_ClientThread == null || _ClientThread.ThreadState != ThreadState.Running)
			{
				_ClientThread = new Thread(new ThreadStart(delegate()
					{
						MtaQueuedMessageCollection messagesToSend = DAL.MtaMessageDB.PickupForSending(10);
						bool done = false;

						while (!done)
						{
							for (int i = 0; i < messagesToSend.Count; i++)
							{
								try
								{
									SendMessage(messagesToSend[i]);
								}
								finally
								{
									messagesToSend[i].Dispose();
								}
							}

							messagesToSend = DAL.MtaMessageDB.PickupForSending(10);
							if (messagesToSend.Count == 0)
								Thread.Sleep(15 * 1000);
						}
					}));
				_ClientThread.Start();
			}
		}

		private static void SendMessage(MtaQueuedMessage msg)
		{
			MailAddress rcptTo = msg.RcptTo[0];
			MailAddress mailFrom = msg.MailFrom;
			
			//DNS.MXRecord[] mxs = DNS.DNSManager.GetMXRecords(rcptTo.Host);
			DNS.MXRecord[] mxs = new DNS.MXRecord[] { new DNS.MXRecord("RA", 1, uint.MaxValue) };
			
			// If mxs is null then there are no MX records.
			if (mxs == null)
			{
				msg.HandleDeliveryFail("No MX in DNS.");
				return;
			}



			
			using (TcpClient tcpClient = new TcpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(msg.OutboundIP), 0)))
			{
				for (int i = 0; i < mxs.Length; i++)
				{
					try
					{
						tcpClient.Connect(mxs[i].Host, 25);
					}
					catch(SocketException)
					{
						// Failed to connect to MX
						if (i == (mxs.Length - 1))
						{
							// There are no more to test
							msg.HandleDeliveryDeferral("Connect failed");
							return;
						}
						else
							continue;
					}


					try
					{
						Action<SmtpStreamHandler, string> handleSmtpError = new Action<SmtpStreamHandler, string>(delegate(SmtpStreamHandler streamHandler, string smtpRespose)
						{
							// If smtpRespose starts with 5 then perm error should cause fail
							if (smtpRespose.StartsWith("5"))
								msg.HandleDeliveryFail(smtpRespose);
							else
								// Otherwise message is deferred
								msg.HandleDeliveryDeferral(smtpRespose);
						});
						

						// We have connected to the MX, Say HELLO
						SmtpStreamHandler smtpStream = new SmtpStreamHandler(tcpClient);
						string response = string.Empty;
						response = smtpStream.ReadAllLines();
						if (!response.StartsWith("2"))
						{
							handleSmtpError(smtpStream, response);
							return;
						}


						smtpStream.WriteLine("HELO " + System.Net.Dns.GetHostEntry(msg.OutboundIP).HostName);
						response = smtpStream.ReadAllLines();
						if (!response.StartsWith("250"))
						{
							handleSmtpError(smtpStream, response);
							return;
						}
					
						smtpStream.WriteLine("MAIL FROM: <" + mailFrom.Address + ">");
						response = smtpStream.ReadAllLines();
						if (!response.StartsWith("250"))
						{
							handleSmtpError(smtpStream, response);
							return;
						}

						smtpStream.WriteLine("RCPT TO: <" + rcptTo.Address + ">");
						response = smtpStream.ReadAllLines();
						if (!response.StartsWith("250"))
						{
							handleSmtpError(smtpStream, response);
							return;
						}

						smtpStream.WriteLine("DATA");
						response = smtpStream.ReadAllLines();
						if (!response.StartsWith("354"))
						{
							handleSmtpError(smtpStream, response);
							return;
						}

						string[] dataLines = msg.Data.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
						for (int l = 0; l < dataLines.Length; l++)
						{
							smtpStream.WriteLine(dataLines[l], false);
						}
						smtpStream.Write(Environment.NewLine + "." + Environment.NewLine, false);
						response = smtpStream.ReadAllLines();
						if (!response.StartsWith("250"))
						{
							handleSmtpError(smtpStream, response);
							return;
						}

						smtpStream.WriteLine("QUIT");
						msg.HandleDeliverySuccess();
						return;
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.Message);
						return;
					}
				}
			}
		}
	}
}