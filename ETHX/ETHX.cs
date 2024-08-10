using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

// [assembly: ContractTitle("optional contract title")]
// [assembly: ContractDescription("optional contract description")]
// [assembly: ContractVersion("optional contract version")]
// [assembly: ContractAuthor("optional contract author")]
// [assembly: ContractEmail("optional contract email")]
[assembly: Features(ContractPropertyState.HasStorage | ContractPropertyState.HasDynamicInvoke | ContractPropertyState.Payable)]

namespace MockNep5
{
    public class ETHX : SmartContract
    {
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;
        public static event Action<BigInteger, byte[], byte[], BigInteger> LockEvent;
        public static event Action<byte[], BigInteger> UnlockEvent;

        // Dynamic Call
        private delegate object DynCall(string method, object[] args); // dynamic call

        private static readonly byte[] CCMCScriptHash = "".HexToBytes();

        private static readonly byte[] Owner = "".ToScriptHash(); //Owner Address
        private static readonly byte[] ZERO_ADDRESS = "0000000000000000000000000000000000000000".HexToBytes();

        private static readonly BigInteger total_amount = new BigInteger("000000e4d20cc8dcd2b75200".HexToBytes()); // total token amount, 1*10^18
        //private const ulong max = ulong.MaxValue; //18446744073709551615

        // StorageMap contract, key: "totalSupply", value: total_amount
        // StorageMap contract, key: "owner", value: owner, byte[20], legal address
        // StorageMap contract, key: "paused", value: 1 (true) or 0 (false)
        // StorageMap asset, key: account, value: balance

        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return Runtime.CheckWitness(GetOwner());
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                // Contract deployment
                if (method == "deploy") 
                    return Deploy();
                if (method == "isDeployed")
                    return IsDeployed();

                // Cross chain
                if (method == "bindContractAddress")
                    return BindContractAddress((BigInteger)args[0], (byte[])args[1]);
                if (method == "getContractAddress")
                    return GetContractAddress((BigInteger)args[0]);
                if (method == "lock")
                    return Lock((byte[])args[0], (BigInteger)args[1], (byte[])args[2], (BigInteger)args[3]);
                if (method == "unlock")
                    return Unlock((byte[])args[0], (byte[])args[1], (BigInteger)args[2], callscript);

                // NEP5 standard methods
                if (method == "balanceOf") return BalanceOf((byte[])args[0]);

                if (method == "decimals") return Decimals();

                if (method == "name") return Name();

                if (method == "symbol") return Symbol();

                if (method == "totalSupply") return TotalSupply();

                if (method == "transfer") return Transfer((byte[])args[0], (byte[])args[1], (BigInteger)args[2], callscript);

                // Owner management
                if (method == "transferOwnership")
                    return TransferOwnership((byte[])args[0]);
                if (method == "getOwner")
                    return GetOwner();

                // Contract management
                if (method == "supportedStandards") 
                    return SupportedStandards();
                if (method == "pause")
                    return Pause();
                if (method == "unpause")
                    return Unpause();
                if (method == "isPaused")
                    return IsPaused();
                if (method == "upgrade")
                {
                    Runtime.Notify("In upgrade");
                    if (args.Length < 9) return false;
                    byte[] script = (byte[])args[0];
                    byte[] plist = (byte[])args[1];
                    byte rtype = (byte)args[2];
                    ContractPropertyState cps = (ContractPropertyState)args[3];
                    string name = (string)args[4];
                    string version = (string)args[5];
                    string author = (string)args[6];
                    string email = (string)args[7];
                    string description = (string)args[8];
                    return Upgrade(script, plist, rtype, cps, name, version, author, email, description);
                }
            }
            return false;
        }

        #region -----Contract deployment-----
        [DisplayName("deploy")]
        public static bool Deploy()
        {
            if (!Runtime.CheckWitness(Owner))
            {
                Runtime.Notify("Only owner can deploy this contract.");
                return false;
            }
            if (IsDeployed())
            {
                Runtime.Notify("Already deployed");
                return false;
            }

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            contract.Put("totalSupply", total_amount);
            contract.Put("owner", Owner);

            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            asset.Put(Owner, total_amount);
            Transferred(null, Owner, total_amount);
            return true;
        }

        [DisplayName("isDeployed")]
        public static bool IsDeployed()
        {
            // if totalSupply has value, means deployed
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            byte[] total_supply = contract.Get("totalSupply");
            return total_supply.Length != 0;
        }
        #endregion

        #region -----Cross chain-----
        [DisplayName("bindContractAddress")]
        public static bool BindContractAddress(BigInteger toChainId, byte[] contractAddr)
        {
            if (!Runtime.CheckWitness(GetOwner()))
            {
                Runtime.Notify("Only owner can deploy this contract.");
                return false;
            }
            StorageMap assetHash = Storage.CurrentContext.CreateMap(nameof(assetHash));
            assetHash.Put(toChainId.AsByteArray(), contractAddr);
            return true;
        }

        [DisplayName("getContractAddress")]
        public static byte[] GetContractAddress(BigInteger toChainId)
        {
            StorageMap assetHash = Storage.CurrentContext.CreateMap(nameof(assetHash));
            return assetHash.Get(toChainId.AsByteArray());
        }

        [DisplayName("lock")]
        public static bool Lock(byte[] fromAddress, BigInteger toChainId, byte[] toAddress, BigInteger amount)
        {
            // check parameters
            if (!IsAddress(fromAddress))
            {
                Runtime.Notify("The parameter fromAddress SHOULD be a legal address.");
                return false;
            }
            if (toAddress.Length == 0)
            {
                Runtime.Notify("The parameter toAddress SHOULD not be empty.");
                return false;
            }
            if (amount < 0)
            {
                Runtime.Notify("The parameter amount SHOULD not be less than 0.");
                return false;
            }
            // more checks
            if (!Runtime.CheckWitness(fromAddress))
            {
                Runtime.Notify("Authorization failed.");
                return false;
            }
            if (IsPaused())
            {
                Runtime.Notify("The contract is paused.");
                return false;
            }

            // lock asset
            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            var balance = asset.Get(fromAddress).AsBigInteger();
            if (balance < amount)
            {
                Runtime.Notify("Not enough balance to lock.");
                return false;
            }
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            var totalSupply = contract.Get("totalSupply").AsBigInteger();
            if (totalSupply < amount)
            {
                Runtime.Notify("Not enough supply to lock.");
                return false;
            }
            asset.Put(fromAddress, balance - amount);
            contract.Put("totalSupply", totalSupply - amount);

            // construct args for the corresponding asset contract on target chain
            var inputBytes = SerializeArgs(toAddress, amount);
            var toContract = GetContractAddress(toChainId);
            // constrct params for CCMC 
            var param = new object[] { toChainId, toContract, "unlock", inputBytes };

            // dynamic call CCMC
            var ccmc = (DynCall)CCMCScriptHash.ToDelegate();
            var success = (bool)ccmc("CrossChain", param);
            if (!success)
            {
                Runtime.Notify("Failed to call CCMC.");
                return false;
            }

            LockEvent(toChainId, fromAddress, toAddress, amount);
            return true;
        }

#if DEBUG
        [DisplayName("unlock")] //Only for ABI file
        public static bool Unlock(byte[] inputBytes, byte[] fromContract, BigInteger fromChainId) => true;
#endif

        // Methods of actual execution
        // used to unlock asset from proxy contract
        private static bool Unlock(byte[] inputBytes, byte[] fromContract, BigInteger fromChainId, byte[] caller)
        {
            //only allowed to be called by CCMC
            if (caller.AsBigInteger() != CCMCScriptHash.AsBigInteger())
            {
                Runtime.Notify("Only allowed to be called by CCMC");
                return false;
            }

            byte[] storedContract = GetContractAddress(fromChainId);

            // check the fromContract is stored, so we can trust it
            if (fromContract.AsBigInteger() != storedContract.AsBigInteger())
            {
                Runtime.Notify(fromContract);
                Runtime.Notify(fromChainId);
                Runtime.Notify(storedContract);
                Runtime.Notify("From contract address not found.");
                return false;
            }

            // parse the args bytes constructed in source chain proxy contract, passed by multi-chain
            object[] results = DeserializeArgs(inputBytes);
            var toAddress = (byte[])results[0];
            var amount = (BigInteger)results[1];
            if (!IsAddress(toAddress))
            {
                Runtime.Notify("ToChain Account address SHOULD be a legal address.");
                return false;
            }
            if (amount < 0)
            {
                Runtime.Notify("ToChain Amount SHOULD not be less than 0.");
                return false;
            }

            // unlock asset 
            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            var balance = asset.Get(toAddress).AsBigInteger();
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            var totalSupply = contract.Get("totalSupply").AsBigInteger();
            asset.Put(toAddress, balance + amount);
            contract.Put("totalSupply", totalSupply + amount);

            UnlockEvent(toAddress, amount);
            return true;
        }
        #endregion

        #region -----NEP5 standard methods-----
        [DisplayName("balanceOf")]
        public static BigInteger BalanceOf(byte[] account)
        {
            if (!IsAddress(account))
            {
                Runtime.Notify("The parameter account SHOULD be a legal address.");
                return 0;
            }
            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            return asset.Get(account).AsBigInteger();
        }

        [DisplayName("decimals")]
        public static byte Decimals() => 18;

        [DisplayName("name")]
        public static string Name() => "ETHX_NEP5"; //name of the token

        [DisplayName("symbol")]
        public static string Symbol() => "ETHX"; //symbol of the token

        [DisplayName("totalSupply")]
        public static BigInteger TotalSupply()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            return contract.Get("totalSupply").AsBigInteger();
        }
#if DEBUG
        [DisplayName("transfer")] //Only for ABI file
        public static bool Transfer(byte[] from, byte[] to, BigInteger amount) => true;
#endif
        //Methods of actual execution
        private static bool Transfer(byte[] from, byte[] to, BigInteger amount, byte[] callscript)
        {
            if (IsPaused())
            {
                Runtime.Notify("ETHX contract is paused.");
                return false;
            }
            //Check parameters
            if (!IsAddress(from) || !IsAddress(to))
            {
                Runtime.Notify("The parameters from and to SHOULD be legal addresses.");
                return false;
            }
            if (amount <= 0)
            {
                Runtime.Notify("The parameter amount MUST be greater than 0.");
                return false;
            }
            if (!IsPayable(to))
            {
                Runtime.Notify("The to account is not payable.");
                return false;
            }
            if (!Runtime.CheckWitness(from) && from.AsBigInteger() != callscript.AsBigInteger())
            {
                // either the tx is signed by "from" or is called by "from"
                Runtime.Notify("Not authorized by the from account");
                return false;
            }

            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            var fromAmount = asset.Get(from).AsBigInteger();
            if (fromAmount < amount)
            {
                Runtime.Notify("Insufficient funds");
                return false;
            }
            if (from == to)
                return true;

            //Reduce payer balances
            if (fromAmount == amount)
                asset.Delete(from);
            else
                asset.Put(from, fromAmount - amount);

            //Increase the payee balance
            var toAmount = asset.Get(to).AsBigInteger();
            asset.Put(to, toAmount + amount);

            Transferred(from, to, amount);
            return true;
        }
        #endregion

        #region -----Owner Management-----
        [DisplayName("transferOwnership")]
        public static bool TransferOwnership(byte[] newOwner)
        {
            // transfer contract ownership from current owner to a new owner
            if (!Runtime.CheckWitness(GetOwner()))
            {
                Runtime.Notify("Only allowed to be called by owner.");
                return false;
            }
            if (!IsAddress(newOwner))
            {
                Runtime.Notify("The parameter newOwner SHOULD be a legal address.");
                return false;
            }

            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            contract.Put("owner", newOwner);
            return true;
        }

        [DisplayName("getOwner")]
        public static byte[] GetOwner()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            var owner = contract.Get("owner");
            return owner;
        }
        #endregion

        #region -----Contract management-----
        [DisplayName("supportedStandards")]
        public static string[] SupportedStandards() => new string[] { "NEP-5", "NEP-7", "NEP-10" };

        [DisplayName("pause")]
        public static bool Pause()
        {
            // Set the smart contract to paused state, the token can not be transfered, approved.
            // Only can invoke some get interface, like getOwner.
            if (!Runtime.CheckWitness(GetOwner()))
            {
                Runtime.Notify("Only allowed to be called by owner.");
                return false;
            }
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            contract.Put("paused", 1);
            return true;
        }

        [DisplayName("unpause")]
        public static bool Unpause()
        {
            if (!Runtime.CheckWitness(GetOwner()))
            {
                Runtime.Notify("Only allowed to be called by owner.");
                return false;
            }
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            contract.Put("paused", 0);
            return true;
        }

        [DisplayName("isPaused")]
        public static bool IsPaused()
        {
            StorageMap contract = Storage.CurrentContext.CreateMap(nameof(contract));
            return contract.Get("paused").AsBigInteger() != 0;
        }

        [DisplayName("upgrade")]
        public static bool Upgrade(byte[] newScript, byte[] paramList, byte returnType, ContractPropertyState cps,
            string name, string version, string author, string email, string description)
        {
            if (!Runtime.CheckWitness(GetOwner()))
            {
                Runtime.Notify("Only allowed to be called by owner.");
                return false;
            }
            var contract = Contract.Migrate(newScript, paramList, returnType, cps, name, version, author, email, description);
            Runtime.Notify("Proxy contract upgraded");
            return true;
        }
        #endregion

        #region -----Helper methods-----
        private static bool IsAddress(byte[] address)
        {
            return address.Length == 20 && address.AsBigInteger() != ZERO_ADDRESS.AsBigInteger();
        }

        private static bool IsPayable(byte[] to)
        {
            var c = Blockchain.GetContract(to);
            return c == null || c.IsPayable;
        }
        #endregion

        #region For Deserialization
        //[DisplayName("testDeserialize")]
        private static object[] DeserializeArgs(byte[] buffer)
        {
            var offset = 0;
            var res = ReadVarBytes(buffer, offset);
            var toAddress = res[0];

            res = ReadUint255(buffer, (int)res[1]);
            var amount = res[0];

            return new object[] { toAddress, amount };
        }

        private static object[] ReadUint255(byte[] buffer, int offset)
        {
            if (offset + 32 > buffer.Length)
            {
                Runtime.Notify("Length is not long enough");
                return new object[] { 0, -1 };
            }
            return new object[] { buffer.Range(offset, 32).ToBigInteger(), offset + 32 };
        }

        // return [BigInteger: value, int: offset]
        private static object[] ReadVarInt(byte[] buffer, int offset)
        {
            var res = ReadBytes(buffer, offset, 1); // read the first byte
            var fb = (byte[])res[0];
            if (fb.Length != 1)
            {
                Runtime.Notify("Wrong length");
                return new object[] { 0, -1 };
            }
            var newOffset = (int)res[1];
            if (fb == new byte[] { 0xFD })
            {
                return new object[] { buffer.Range(newOffset, 2).ToBigInteger(), newOffset + 2 };
            }
            else if (fb == new byte[] { 0xFE })
            {
                return new object[] { buffer.Range(newOffset, 4).ToBigInteger(), newOffset + 4 };
            }
            else if (fb == new byte[] { 0xFF })
            {
                return new object[] { buffer.Range(newOffset, 8).ToBigInteger(), newOffset + 8 };
            }
            else
            {
                return new object[] { fb.ToBigInteger(), newOffset };
            }
        }

        // return [byte[], new offset]
        private static object[] ReadVarBytes(byte[] buffer, int offset)
        {
            var res = ReadVarInt(buffer, offset);
            var count = (int)res[0];
            var newOffset = (int)res[1];
            return ReadBytes(buffer, newOffset, count);
        }

        // return [byte[], new offset]
        private static object[] ReadBytes(byte[] buffer, int offset, int count)
        {
            if (offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
            return new object[] { buffer.Range(offset, count), offset + count };
        }
        #endregion

        #region For Serialization
        //[DisplayName("testSerialize")]
        private static byte[] SerializeArgs(byte[] address, BigInteger amount)
        {
            var buffer = new byte[] { };
            buffer = WriteVarBytes(address, buffer);
            buffer = WriteUint255(amount, buffer);
            return buffer;
        }

        private static byte[] WriteUint255(BigInteger value, byte[] source)
        {
            if (value < 0)
            {
                Runtime.Notify("Value out of range of uint255");
                return source;
            }
            var v = PadRight(value.ToByteArray(), 32);
            return source.Concat(v); // no need to concat length, fix 32 bytes
        }

        private static byte[] WriteVarInt(BigInteger value, byte[] Source)
        {
            if (value < 0)
            {
                return Source;
            }
            else if (value < 0xFD)
            {
                var v = PadRight(value.ToByteArray(), 1);
                return Source.Concat(v);
            }
            else if (value <= 0xFFFF) // 0xff, need to pad 1 0x00
            {
                byte[] length = new byte[] { 0xFD };
                var v = PadRight(value.ToByteArray(), 2);
                return Source.Concat(length).Concat(v);
            }
            else if (value <= 0XFFFFFFFF) //0xffffff, need to pad 1 0x00 
            {
                byte[] length = new byte[] { 0xFE };
                var v = PadRight(value.ToByteArray(), 4);
                return Source.Concat(length).Concat(v);
            }
            else //0x ff ff ff ff ff, need to pad 3 0x00
            {
                byte[] length = new byte[] { 0xFF };
                var v = PadRight(value.ToByteArray(), 8);
                return Source.Concat(length).Concat(v);
            }
        }

        private static byte[] WriteVarBytes(byte[] value, byte[] Source)
        {
            return WriteVarInt(value.Length, Source).Concat(value);
        }

        // add padding zeros on the right
        private static byte[] PadRight(byte[] value, int length)
        {
            var l = value.Length;
            if (l > length)
                return value.Range(0, length);
            for (int i = 0; i < length - l; i++)
            {
                value = value.Concat(new byte[] { 0x00 });
            }
            return value;
        }
        #endregion
    }
}
