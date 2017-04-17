﻿using AntShares.Consensus;
using AntShares.Core;
using AntShares.Implementations.Blockchains.LevelDB;
using AntShares.Implementations.Wallets.EntityFramework;
using AntShares.IO;
using AntShares.Network;
using AntShares.Network.RPC;
using AntShares.Services;
using AntShares.Wallets;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Threading.Tasks;

namespace AntShares.Shell
{
    internal class MainService : ConsoleServiceBase
    {
        private RpcServerWithWallet rpc;
        private ConsensusService consensus;

        protected LocalNode LocalNode { get; private set; }
        protected override string Prompt => "ant";
        public override string ServiceName => "AntSharesDaemon";

        private void ImportBlocks(Stream stream)
        {
            LevelDBBlockchain blockchain = (LevelDBBlockchain)Blockchain.Default;
            blockchain.VerifyBlocks = false;
            using (BinaryReader r = new BinaryReader(stream))
            {
                uint count = r.ReadUInt32();
                for (int height = 0; height < count; height++)
                {
                    byte[] array = r.ReadBytes(r.ReadInt32());
                    if (height > Blockchain.Default.Height)
                    {
                        Block block = array.AsSerializable<Block>();
                        Blockchain.Default.AddBlock(block);
                    }
                }
            }
            blockchain.VerifyBlocks = true;
        }

        protected override bool OnCommand(string[] args)
        {
            switch (args[0].ToLower())
            {
                case "create":
                    return OnCreateCommand(args);
                case "export":
                    return OnExportCommand(args);
                case "help":
                    return OnHelpCommand(args);
                case "import":
                    return OnImportCommand(args);
                case "list":
                    return OnListCommand(args);
                case "open":
                    return OnOpenCommand(args);
                case "rebuild":
                    return OnRebuildCommand(args);
                case "refresh":
                    return OnRefreshCommand(args);
                case "send":
                    return OnSendCommand(args);
                case "show":
                    return OnShowCommand(args);
                case "start":
                    return OnStartCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnCreateCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "address":
                    return OnCreateAddressCommand(args);
                case "wallet":
                    return OnCreateWalletCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnCreateAddressCommand(string[] args)
        {
            if (Program.Wallet == null)
            {
                Console.WriteLine("You have to open the wallet first.");
                return true;
            }
            if (args.Length > 3)
            {
                Console.WriteLine("error");
                return true;
            }
            ushort count = 1;
            if (args.Length >= 3)
                count = ushort.Parse(args[2]);
            List<string> addresses = new List<string>();
            for (int i = 1; i <= count; i++)
            {
                Account account = Program.Wallet.CreateAccount();
                Contract contract = Program.Wallet.GetContracts(account.PublicKeyHash).First(p => p.IsStandard);
                addresses.Add(contract.Address);
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"[{i}/{count}]");
            }
            Console.WriteLine();
            string path = "address.txt";
            Console.WriteLine($"export addresses to {path}");
            File.WriteAllLines(path, addresses);
            return true;
        }

        private bool OnCreateWalletCommand(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("error");
                return true;
            }
            using (SecureString password = ReadSecureString("password"))
            using (SecureString password2 = ReadSecureString("password"))
            {
                if (!password.CompareTo(password2))
                {
                    Console.WriteLine("error");
                    return true;
                }
                Program.Wallet = UserWallet.Create(args[2], password);
            }
            Contract contract = Program.Wallet.GetContracts().First(p => p.IsStandard);
            Account account = Program.Wallet.GetAccount(contract.PublicKeyHash);
            Console.WriteLine($"address: {contract.Address}");
            Console.WriteLine($" pubkey: {account.PublicKey.EncodePoint(true).ToHexString()}");
            return true;
        }

        private bool OnExportCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "blocks":
                    return OnExportBlocksCommand(args);
                case "key":
                    return OnExportKeyCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnExportBlocksCommand(string[] args)
        {
            if (args.Length > 3)
            {
                Console.WriteLine("error");
                return true;
            }
            string path = args.Length >= 3 ? args[2] : "chain.acc";
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                uint count = Blockchain.Default.Height + 1;
                fs.Write(BitConverter.GetBytes(count), 0, sizeof(uint));
                for (uint i = 0; i < count; i++)
                {
                    Block block = Blockchain.Default.GetBlock(i);
                    byte[] array = block.ToArray();
                    fs.Write(BitConverter.GetBytes(array.Length), 0, sizeof(int));
                    fs.Write(array, 0, array.Length);
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write($"[{i + 1}/{count}]");
                }
            }
            Console.WriteLine();
            return true;
        }

        private bool OnExportKeyCommand(string[] args)
        {
            if (Program.Wallet == null)
            {
                Console.WriteLine("You have to open the wallet first.");
                return true;
            }
            if (args.Length < 2 || args.Length > 4)
            {
                Console.WriteLine("error");
                return true;
            }
            UInt160 scriptHash = null;
            string path = null;
            if (args.Length == 3)
            {
                try
                {
                    scriptHash = Wallet.ToScriptHash(args[2]);
                }
                catch (FormatException)
                {
                    path = args[2];
                }
            }
            else if (args.Length == 4)
            {
                scriptHash = Wallet.ToScriptHash(args[2]);
                path = args[3];
            }
            using (SecureString password = ReadSecureString("password"))
            {
                if (password.Length == 0)
                {
                    Console.WriteLine("cancelled");
                    return true;
                }
                if (!Program.Wallet.VerifyPassword(password))
                {
                    Console.WriteLine("Incorrect password");
                    return true;
                }
            }
            IEnumerable<Account> accounts;
            if (scriptHash == null)
                accounts = Program.Wallet.GetAccounts();
            else
                accounts = new[] { Program.Wallet.GetAccountByScriptHash(scriptHash) };
            if (path == null)
                foreach (Account account in accounts)
                    Console.WriteLine(account.Export());
            else
                File.WriteAllLines(path, accounts.Select(p => p.Export()));
            return true;
        }

        private bool OnHelpCommand(string[] args)
        {
            Console.Write(
                "Normal Commands:\n" +
                "\tversion\n" +
                "\thelp\n" +
                "\tclear\n" +
                "\texit\n" +
                "Wallet Commands:\n" +
                "\tcreate wallet <path>\n" +
                "\topen wallet <path>\n" +
                "\trebuild index\n" +
                "\tlist account\n" +
                "\tlist address\n" +
                "\tlist asset\n" +
                "\tcreate address [n=1]\n" +
                "\timport key <wif|path>\n" +
                "\texport key [address] [path]\n" +
                "\tsend <id|alias> <address> <value> [fee=0]\n" +
                "Node Commands:\n" +
                "\tshow state\n" +
                "\tshow node\n" +
                "\tshow pool\n" +
                "Advanced Commands:\n" +
                "\tstart consensus\n");
            return true;
        }

        private bool OnImportCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "key":
                    return OnImportKeyCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnImportKeyCommand(string[] args)
        {
            if (args.Length > 3)
            {
                Console.WriteLine("error");
                return true;
            }
            byte[] prikey = null;
            try
            {
                prikey = Wallet.GetPrivateKeyFromWIF(args[2]);
            }
            catch (FormatException) { }
            if (prikey == null)
            {
                string[] lines = File.ReadAllLines(args[2]);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Length == 64)
                        prikey = lines[i].HexToBytes();
                    else
                        prikey = Wallet.GetPrivateKeyFromWIF(lines[i]);
                    Program.Wallet.CreateAccount(prikey);
                    Array.Clear(prikey, 0, prikey.Length);
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write($"[{i + 1}/{lines.Length}]");
                }
                Console.WriteLine();
            }
            else
            {
                Account account = Program.Wallet.CreateAccount(prikey);
                Array.Clear(prikey, 0, prikey.Length);
                Contract contract = Program.Wallet.GetContracts(account.PublicKeyHash).First(p => p.IsStandard);
                Console.WriteLine($"address: {contract.Address}");
                Console.WriteLine($" pubkey: {account.PublicKey.EncodePoint(true).ToHexString()}");
            }
            return true;
        }

        private bool OnListCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "account":
                    return OnListAccountCommand(args);
                case "address":
                    return OnListAddressCommand(args);
                case "asset":
                    return OnListAssetCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnListAccountCommand(string[] args)
        {
            if (Program.Wallet == null) return true;
            foreach (Account account in Program.Wallet.GetAccounts())
            {
                Console.WriteLine(account.PublicKey);
            }
            return true;
        }

        private bool OnListAddressCommand(string[] args)
        {
            if (Program.Wallet == null) return true;
            foreach (Contract contract in Program.Wallet.GetContracts())
            {
                Console.WriteLine($"{contract.Address}\t{(contract.IsStandard ? "Standard" : "Nonstandard")}");
            }
            return true;
        }

        private bool OnListAssetCommand(string[] args)
        {
            if (Program.Wallet == null) return true;
            foreach (var item in Program.Wallet.GetCoins().Where(p => !p.State.HasFlag(CoinState.Spent)).GroupBy(p => p.Output.AssetId, (k, g) => new
            {
                Asset = (RegisterTransaction)Blockchain.Default.GetTransaction(k),
                Balance = g.Sum(p => p.Output.Value),
                Confirmed = g.Where(p => p.State.HasFlag(CoinState.Confirmed)).Sum(p => p.Output.Value)
            }))
            {
                Console.WriteLine($"       id:{item.Asset.Hash}");
                Console.WriteLine($"     name:{item.Asset.GetName()}");
                Console.WriteLine($"  balance:{item.Balance}");
                Console.WriteLine($"confirmed:{item.Confirmed}");
                Console.WriteLine();
            }
            return true;
        }

        private bool OnOpenCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "wallet":
                    return OnOpenWalletCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        //TODO: 目前没有想到其它安全的方法来保存密码
        //所以只能暂时手动输入，但如此一来就不能以服务的方式启动了
        //未来再想想其它办法，比如采用智能卡之类的
        private bool OnOpenWalletCommand(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("error");
                return true;
            }
            using (SecureString password = ReadSecureString("password"))
            {
                if (password.Length == 0)
                {
                    Console.WriteLine("cancelled");
                    return true;
                }
                try
                {
                    Program.Wallet = UserWallet.Open(args[2], password);
                }
                catch
                {
                    Console.WriteLine($"failed to open file \"{args[2]}\"");
                    return true;
                }
            }
            return true;
        }

        private bool OnRebuildCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "index":
                    return OnRebuildIndexCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnRebuildIndexCommand(string[] args)
        {
            if (Program.Wallet == null) return true;
            Program.Wallet.Rebuild();
            return true;
        }

        private bool OnRefreshCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "policy":
                    return OnRefreshPolicyCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnRefreshPolicyCommand(string[] args)
        {
            if (consensus != null) return true;
            consensus.RefreshPolicy();
            return true;
        }

        private bool OnSendCommand(string[] args)
        {
            if (Program.Wallet == null)
            {
                Console.WriteLine("You have to open the wallet first.");
                return true;
            }
            if (args.Length < 4 || args.Length > 5)
            {
                Console.WriteLine("error");
                return true;
            }
            UInt256 assetId;
            switch (args[1].ToLower())
            {
                case "ans":
                    assetId = Blockchain.AntShare.Hash;
                    break;
                case "anc":
                    assetId = Blockchain.AntCoin.Hash;
                    break;
                default:
                    assetId = UInt256.Parse(args[1]);
                    break;
            }
            UInt160 scriptHash = Wallet.ToScriptHash(args[2]);
            Fixed8 value = Fixed8.Parse(args[3]);
            Fixed8 fee = args.Length >= 5 ? Fixed8.Parse(args[4]) : Fixed8.Zero;
            ContractTransaction tx = Program.Wallet.MakeTransaction(new ContractTransaction
            {
                Outputs = new[]
                {
                    new TransactionOutput
                    {
                        AssetId = assetId,
                        Value = value,
                        ScriptHash = scriptHash
                    }
                }
            }, fee);
            if (tx == null)
            {
                Console.WriteLine("Insufficient funds");
                return true;
            }
            using (SecureString password = ReadSecureString("password"))
            {
                if (password.Length == 0)
                {
                    Console.WriteLine("cancelled");
                    return true;
                }
                if (!Program.Wallet.VerifyPassword(password))
                {
                    Console.WriteLine("Incorrect password");
                    return true;
                }
            }
            SignatureContext context = new SignatureContext(tx);
            Program.Wallet.Sign(context);
            if (context.Completed)
            {
                tx.Scripts = context.GetScripts();
                Program.Wallet.SaveTransaction(tx);
                LocalNode.Relay(tx);
                Console.WriteLine($"TXID: {tx.Hash}");
            }
            else
            {
                Console.WriteLine("SignatureContext:");
                Console.WriteLine(context.ToString());
            }
            return true;
        }

        private bool OnShowCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "node":
                    return OnShowNodeCommand(args);
                case "pool":
                    return OnShowPoolCommand(args);
                case "state":
                    return OnShowStateCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnShowNodeCommand(string[] args)
        {
            RemoteNode[] nodes = LocalNode.GetRemoteNodes();
            for (int i = 0; i < nodes.Length; i++)
            {
                Console.WriteLine($"{nodes[i].RemoteEndpoint.Address} port:{nodes[i].RemoteEndpoint.Port} listen:{nodes[i].ListenerEndpoint?.Port ?? 0} [{i + 1}/{nodes.Length}]");
            }
            return true;
        }

        private bool OnShowPoolCommand(string[] args)
        {
            foreach (Transaction tx in LocalNode.GetMemoryPool())
            {
                Console.WriteLine($"{tx.Hash} {tx.GetType().Name}");
            }
            return true;
        }

        private bool OnShowStateCommand(string[] args)
        {
            Console.WriteLine($"Height: {Blockchain.Default.Height}/{Blockchain.Default.HeaderHeight}, Nodes: {LocalNode.RemoteNodeCount}");
            return true;
        }

        protected internal override void OnStart(string[] args)
        {
            Blockchain.RegisterBlockchain(new LevelDBBlockchain(Settings.Default.DataDirectoryPath));
            LocalNode = new LocalNode();
            Task.Run(() =>
            {
                const string acc_path = "chain.acc";
                const string acc_zip_path = acc_path + ".zip";
                if (File.Exists(acc_path))
                {
                    using (FileStream fs = new FileStream(acc_path, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        ImportBlocks(fs);
                    }
                    File.Delete(acc_path);
                }
                else if (File.Exists(acc_zip_path))
                {
                    using (FileStream fs = new FileStream(acc_zip_path, FileMode.Open, FileAccess.Read, FileShare.None))
                    using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    using (Stream zs = zip.GetEntry(acc_path).Open())
                    {
                        ImportBlocks(zs);
                    }
                    File.Delete(acc_zip_path);
                }
                LocalNode.Start(Settings.Default.NodePort);
                if (args.Length >= 1 && args[0] == "/rpc")
                {
                    rpc = new RpcServerWithWallet(LocalNode);
                    rpc.Start(Settings.Default.UriPrefix.OfType<string>().ToArray(), Settings.Default.SslCert, Settings.Default.SslCertPassword);
                }
            });
        }

        private bool OnStartCommand(string[] args)
        {
            switch (args[1].ToLower())
            {
                case "consensus":
                    return OnStartConsensusCommand(args);
                default:
                    return base.OnCommand(args);
            }
        }

        private bool OnStartConsensusCommand(string[] args)
        {
            if (consensus != null) return true;
            if (Program.Wallet == null)
            {
                Console.WriteLine("You have to open the wallet first.");
                return true;
            }
            string log_dictionary = Path.Combine(AppContext.BaseDirectory, "Logs");
            consensus = new ConsensusService(LocalNode, Program.Wallet, log_dictionary);
            ShowPrompt = false;
            consensus.Start();
            return true;
        }

        protected internal override void OnStop()
        {
            if (consensus != null) consensus.Dispose();
            if (rpc != null) rpc.Dispose();
            LocalNode.Dispose();
            Blockchain.Default.Dispose();
        }
    }
}
