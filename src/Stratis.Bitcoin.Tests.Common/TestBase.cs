﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Tests.Common
{
    public class TestBase
    {
        public Network Network { get; protected set; }

        /// <summary>
        /// Initializes logger factory for inherited tests.
        /// </summary>
        public TestBase(Network network)
        {
            this.Network = network;
            var serializer = new DBreezeSerializer();
            serializer.Initialize(this.Network);
        }

        public static string AssureEmptyDir(string dir)
        {
            string uniqueDirName = $"{dir}-{DateTime.UtcNow:ddMMyyyyTHH.mm.ss.fff}";
            Directory.CreateDirectory(uniqueDirName);
            return uniqueDirName;
        }

        /// <summary>
        /// Creates a directory and initializes a <see cref="DataFolder"/> for a test, based on the name of the class containing the test and the name of the test.
        /// </summary>
        /// <param name="caller">The calling object, from which we derive the namespace in which the test is contained.</param>
        /// <param name="callingMethod">The name of the test being executed. A directory with the same name will be created.</param>
        /// <returns>The <see cref="DataFolder"/> that was initialized.</returns>
        public static DataFolder CreateDataFolder(object caller, [System.Runtime.CompilerServices.CallerMemberName] string callingMethod = "")
        {
            string directoryPath = GetTestDirectoryPath(caller, callingMethod);
            var dataFolder = new DataFolder(new NodeSettings(networksSelector: Networks.Networks.Bitcoin, args: new string[] { $"-datadir={AssureEmptyDir(directoryPath)}" }).DataDir);
            return dataFolder;
        }

        /// <summary>
        /// Creates a directory for a test, based on the name of the class containing the test and the name of the test.
        /// </summary>
        /// <param name="caller">The calling object, from which we derive the namespace in which the test is contained.</param>
        /// <param name="callingMethod">The name of the test being executed. A directory with the same name will be created.</param>
        /// <returns>The path of the directory that was created.</returns>
        public static string CreateTestDir(object caller, [System.Runtime.CompilerServices.CallerMemberName] string callingMethod = "")
        {
            string directoryPath = GetTestDirectoryPath(caller, callingMethod);
            return AssureEmptyDir(directoryPath);
        }

        /// <summary>
        /// Creates a directory for a test.
        /// </summary>
        /// <param name="testDirectory">The directory in which the test files are contained.</param>
        /// <returns>The path of the directory that was created.</returns>
        public static string CreateTestDir(string testDirectory)
        {
            string directoryPath = GetTestDirectoryPath(testDirectory);
            return AssureEmptyDir(directoryPath);
        }

        /// <summary>
        /// Gets the path of the directory that <see cref="CreateTestDir(object, string)"/> or <see cref="CreateDataFolder(object, string)"/> would create.
        /// </summary>
        /// <remarks>The path of the directory is of the form TestCase/{testClass}/{testName}.</remarks>
        /// <param name="caller">The calling object, from which we derive the namespace in which the test is contained.</param>
        /// <param name="callingMethod">The name of the test being executed. A directory with the same name will be created.</param>
        /// <returns>The path of the directory.</returns>
        public static string GetTestDirectoryPath(object caller, [System.Runtime.CompilerServices.CallerMemberName] string callingMethod = "")
        {
            return GetTestDirectoryPath(Path.Combine(caller.GetType().Name, callingMethod));
        }

        /// <summary>
        /// Gets the path of the directory that <see cref="CreateTestDir(object, string)"/> or <see cref="CreateDataFolder(object, string)"/> would create.
        /// </summary>
        /// <remarks>The path of the directory is of the form TestCase/{testClass}/{testName}.</remarks>
        /// <param name="testDirectory">The directory in which the test files are contained.</param>
        /// <returns>The path of the directory.</returns>
        public static string GetTestDirectoryPath(string testDirectory)
        {
            return Path.Combine("..", "..", "..", "..", "TestCase", testDirectory);
        }

        public void AppendBlocksToChain(ConcurrentChain chain, IEnumerable<Block> blocks)
        {
            foreach (Block block in blocks)
            {
                if (chain.Tip != null)
                    block.Header.HashPrevBlock = chain.Tip.HashBlock;
                chain.SetTip(block.Header);
            }
        }

        public List<Block> CreateBlocks(int amount, bool bigBlocks = false)
        {
            var blocks = new List<Block>();
            for (int i = 0; i < amount; i++)
            {
                Block block = this.CreateBlock(i);
                block.Header.HashPrevBlock = blocks.LastOrDefault()?.GetHash() ?? this.Network.GenesisHash;
                blocks.Add(block);
            }

            return blocks;
        }

        private Block CreateBlock(int blockNumber, bool bigBlocks = false)
        {
            Block block = this.Network.CreateBlock();

            int transactionCount = bigBlocks ? 1000 : 10;

            for (int j = 0; j < transactionCount; j++)
            {
                Transaction trx = this.Network.CreateTransaction();

                block.AddTransaction(this.Network.CreateTransaction());

                trx.AddInput(new TxIn(Script.Empty));
                trx.AddOutput(Money.COIN + j + blockNumber, new Script(Enumerable.Range(1, 5).SelectMany(index => Guid.NewGuid().ToByteArray())));

                trx.AddInput(new TxIn(Script.Empty));
                trx.AddOutput(Money.COIN + j + blockNumber + 1, new Script(Enumerable.Range(1, 5).SelectMany(index => Guid.NewGuid().ToByteArray())));

                block.AddTransaction(trx);
            }

            block.UpdateMerkleRoot();

            return block;
        }

        public ProvenBlockHeader CreateNewProvenBlockHeaderMock(PosBlock posBlock = null)
        {
            PosBlock block = posBlock == null ? CreatePosBlockMock() : posBlock;
            ProvenBlockHeader provenBlockHeader = ((PosConsensusFactory)this.Network.Consensus.ConsensusFactory).CreateProvenBlockHeader(block);

            return provenBlockHeader;
        }

        public PosBlock CreatePosBlockMock()
        {
            // Create coinstake Tx.
            Transaction previousTx = this.Network.CreateTransaction();
            previousTx.AddOutput(new TxOut());
            Transaction coinstakeTx = this.Network.CreateTransaction();
            coinstakeTx.AddOutput(new TxOut(0, Script.Empty));
            coinstakeTx.AddOutput(new TxOut(50, new Script()));
            coinstakeTx.AddInput(previousTx, 0);
            coinstakeTx.IsCoinStake.Should().BeTrue();
            coinstakeTx.IsCoinBase.Should().BeFalse();

            // Create coinbase Tx.
            Transaction coinBaseTx = this.Network.CreateTransaction();
            coinBaseTx.AddOutput(100, new Script());
            coinBaseTx.AddInput(new TxIn());
            coinBaseTx.IsCoinBase.Should().BeTrue();
            coinBaseTx.IsCoinStake.Should().BeFalse();

            var block = (PosBlock)this.Network.CreateBlock();
            block.AddTransaction(coinBaseTx);
            block.AddTransaction(coinstakeTx);
            block.BlockSignature = new BlockSignature { Signature = new byte[] { 0x2, 0x3 } };

            return block;
        }

        /// <returns>Tip of a created chain of headers.</returns>
        public ChainedHeader BuildChainWithProvenHeaders(int blockCount)
        {
            ChainedHeader currentHeader = ChainedHeadersHelper.CreateGenesisChainedHeader(this.Network);

            for (int i = 1; i < blockCount; i++)
            {
                PosBlock block = this.CreatePosBlockMock();
                ProvenBlockHeader header = ((PosConsensusFactory)this.Network.Consensus.ConsensusFactory).CreateProvenBlockHeader(block);

                header.Nonce = RandomUtils.GetUInt32();
                header.HashPrevBlock = currentHeader.HashBlock;
                header.Bits = Target.Difficulty1;

                ChainedHeader prevHeader = currentHeader;
                currentHeader = new ChainedHeader(header, header.GetHash(), i);

                currentHeader.SetPrivatePropertyValue("Previous", prevHeader);
                prevHeader.Next.Add(currentHeader);
            }

            return currentHeader;
        }

        public SortedDictionary<int, ProvenBlockHeader> ConvertToDictionaryOfProvenHeaders(ChainedHeader tip)
        {
            var headers = new SortedDictionary<int, ProvenBlockHeader>();

            ChainedHeader currentHeader = tip;

            while (currentHeader.Height != 0)
            {
                headers.Add(currentHeader.Height, currentHeader.Header as ProvenBlockHeader);

                currentHeader = currentHeader.Previous;
            }

            return headers;
        }
    }
}