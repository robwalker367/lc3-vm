using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;

namespace lc3_vm
{
    class Program
    {
        private static ushort[] Memory = new ushort[UInt16.MaxValue];

        private enum Register
        {
            R_R0,
            R_R1,
            R_R2,
            R_R3,
            R_R4,
            R_R5,
            R_R6,
            R_R7,
            R_PC,
            R_COND,
            R_COUNT
        }

        private static ushort[] Registers = new ushort[(ushort)Register.R_COUNT];

        private enum InstructionSet
        {
            OP_BR,
            OP_ADD,
            OP_LD,
            OP_ST,
            OP_JSR,
            OP_AND,
            OP_LDR,
            OP_STR,
            OP_RTI,
            OP_NOT,
            OP_LDI,
            OP_STI,
            OP_JMP,
            OP_RES,
            OP_LEA,
            OP_TRAP
        }

        private enum TrapCodes
        {
            TRAP_GETC = 0x20,
            TRAP_OUT = 0x21,
            TRAP_PUTS = 0x22,
            TRAP_IN = 0x23,
            TRAP_PUTSP = 0x24,
            TRAP_HALT = 0x25,
        }
        private enum ConditionFlag
        {
            FL_POS = 1 << 0,
            FL_ZRO = 1 << 1,
            FL_NEG = 1 << 2
        }

        private enum RegisterMemoryMapped
        {
            MR_KBSR = 0xFE00,
            MR_KBDR = 0xFE02
        }

        private static ushort PC_START = 0x3000;

        static ushort MemoryRead(ushort Address)
        {
            if (Address == (ushort)RegisterMemoryMapped.MR_KBSR)
            {
                if (Console.KeyAvailable)
                {
                    Memory[(ushort)RegisterMemoryMapped.MR_KBSR] = (ushort)(1 << 15);
                    Memory[(ushort)RegisterMemoryMapped.MR_KBDR] = (ushort)(Console.ReadKey(true).KeyChar);
                }
                else
                {
                    Memory[(ushort)RegisterMemoryMapped.MR_KBSR] = (ushort)0;
                }
            }
            return Memory[Address];
        }

        static void MemoryWrite(ushort Address, ushort Value)
        {
            Memory[Address] = Value;
        }

        static ushort Swap16(ushort Num)
        {
            return (ushort)((Num << 8) | (Num >> 8));
        }

        static int ReadImageFile(string FilePath)
        {
            using (BinaryReader Reader = new BinaryReader(File.Open(FilePath, FileMode.Open)))
            {
                ushort Origin;
                Origin = Swap16(Reader.ReadUInt16());

                ushort MemoryPointer = Origin;
                while ((Reader.BaseStream.Position != Reader.BaseStream.Length) && (MemoryPointer < UInt16.MaxValue))
                {
                    Memory[MemoryPointer] = Swap16(Reader.ReadUInt16());
                    MemoryPointer++;
                }
            }
            return 1;
        }

        static void UpdateFlags(ushort RegisterIndex)
        {
            if (Registers[RegisterIndex] == 0)
            {
                Registers[(ushort)Register.R_COND] = (ushort)ConditionFlag.FL_ZRO;
            }
            else if (IsNegative(Registers[RegisterIndex]))
            {
                Registers[(ushort)Register.R_COND] = (ushort)ConditionFlag.FL_NEG;
            }
            else
            {
                Registers[(ushort)Register.R_COND] = (ushort)ConditionFlag.FL_POS;
            }
        }

        static ushort SignExtend(ushort Num, int BitCount)
        {
            if (((Num >> (BitCount - 1)) & 1) != 0)
            {
                Num |= (ushort)(0xFFFF << BitCount);
            }
            return Num;
        }

        static bool IsNegative(ushort Num)
        {
            return (Num >> 15) == 1;
        }

        static void HandleTrap(ushort Instruction, ref ushort Running)
        {
            ushort TrapCode = (ushort)(Instruction & 0xFF);
            switch (TrapCode)
            {
                case (ushort)TrapCodes.TRAP_GETC:
                    {
                        Registers[(ushort)Register.R_R0] = (ushort)Console.ReadKey(true).KeyChar;
                        break;
                    }
                case (ushort)TrapCodes.TRAP_OUT:
                    {
                        Console.Out.Write((char)Registers[(ushort)Register.R_R0]);
                        Console.Out.Flush();
                        break;
                    }
                case (ushort)TrapCodes.TRAP_IN:
                    {
                        char C = Console.ReadKey(true).KeyChar;
                        Console.Out.Write(C);
                        Registers[(ushort)Register.R_R0] = (ushort)C;
                        break;
                    }
                case (ushort)TrapCodes.TRAP_PUTS:
                    {
                        ushort StartAddress = Registers[(ushort)Register.R_R0];
                        ushort CurrentChar;
                        while ((CurrentChar = MemoryRead(StartAddress)) != 0x0)
                        {
                            Console.Out.Write((char)CurrentChar);
                            StartAddress++;
                        }
                        Console.Out.Flush();
                        break;
                    }
                case (ushort)TrapCodes.TRAP_PUTSP:
                    {
                        ushort StartAddress = Registers[(ushort)Register.R_R0];
                        ushort CurrentChar;
                        while ((CurrentChar = MemoryRead(StartAddress)) != 0x0)
                        {
                            ushort Char1 = (ushort)(CurrentChar & 0xFF);
                            ushort Char2 = (ushort)(CurrentChar >> 8);
                            Console.Out.Write((char)Char1);
                            Console.Out.Write((char)Char2);
                            StartAddress++;
                        }
                        Console.Out.Flush();
                        break;
                    }
                case (ushort)TrapCodes.TRAP_HALT:
                    {
                        Console.Out.Write("HALT");
                        Console.Out.Flush();
                        Running = 0;
                        break;
                    }
                default:
                    {
                        throw new Exception("Invalid trapcode given.");
                    }
            }
            return;
        }

        static void PerformOperation(ushort Instruction, ref ushort Running)
        {
            ushort Operation = (ushort)(Instruction >> 12);
            switch (Operation)
            {
                case (ushort)InstructionSet.OP_ADD:
                    {
                        ushort DR = (ushort)((Instruction >> 9) & 0x7);
                        ushort SR1 = (ushort)((Instruction >> 6) & 0x7);
                        bool IsImmediateMode = ((Instruction >> 5) & 0x1) == 1;
                        if (IsImmediateMode)
                        {
                            ushort IMM5 = SignExtend((ushort)(Instruction & 0x1F), 5);
                            Registers[DR] = (ushort)(Registers[SR1] + IMM5);
                        }
                        else
                        {
                            ushort SR2 = (ushort)(Instruction & 0x7);
                            Registers[DR] = (ushort)(Registers[SR1] + Registers[SR2]);
                        }
                        UpdateFlags(DR);
                        break;
                    }
                case (ushort)InstructionSet.OP_LDI:
                    {
                        ushort DR = (ushort)((Instruction >> 9) & 0x7);
                        ushort PCOffset = SignExtend((ushort)(Instruction & 0x1FF), 9);
                        Registers[DR] = MemoryRead(MemoryRead((ushort)(Registers[(ushort)Register.R_PC] + PCOffset)));
                        break;
                    }
                case (ushort)InstructionSet.OP_AND:
                    {
                        ushort DR = (ushort)((Instruction >> 9) & 0x7);
                        ushort SR1 = (ushort)((Instruction >> 6) & 0x7);
                        bool IsImmediateMode = ((Instruction >> 5) & 0x1) == 1;
                        if (IsImmediateMode)
                        {
                            ushort IMM5 = SignExtend((ushort)(Instruction & 0x1F), 5);
                            Registers[DR] = (ushort)(Registers[SR1] & IMM5);
                        }
                        else
                        {
                            ushort SR2 = (ushort)(Instruction & 0x7);
                            Registers[DR] = (ushort)(Registers[SR1] & Registers[SR2]);
                        }
                        UpdateFlags(DR);
                        break;
                    }
                case (ushort)InstructionSet.OP_BR:
                    {
                        ushort Condition = (ushort)((Instruction >> 9) & 0x7);
                        ushort PCOffset9 = SignExtend((ushort)(Instruction & 0x1FF), 9);
                        if ((Condition & Registers[(int)Register.R_COND]) != 0)
                        {
                            Registers[(int)Register.R_PC] += PCOffset9;
                        }
                        break;
                    }
                case (ushort)InstructionSet.OP_JMP:
                    {
                        ushort BaseR = (ushort)((Instruction >> 6) & 0x7);
                        Registers[(int)Register.R_PC] = Registers[BaseR];
                        break;
                    }
                case (ushort)InstructionSet.OP_JSR:
                    {
                        Registers[(ushort)Register.R_R7] = Registers[(ushort)Register.R_PC];
                        bool IsJRR = ((Instruction >> 11) & 1) == 1;
                        if (IsJRR)
                        {
                            ushort PCOffset11 = SignExtend((ushort)(Instruction & 0x7FF), 11);
                            Registers[(ushort)Register.R_PC] += PCOffset11;
                        }
                        else
                        {
                            ushort BaseR = (ushort)((Instruction >> 6) & 0x7);
                            Registers[(ushort)Register.R_PC] = Registers[BaseR];
                        }
                        break;
                    }
                case (ushort)InstructionSet.OP_LD:
                    {
                        ushort DR = (ushort)((Instruction >> 9) & 0x7);
                        ushort PCOffset9 = SignExtend((ushort)(Instruction & 0x1FF), 9);
                        Registers[DR] = MemoryRead((ushort)(Registers[(ushort)Register.R_PC] + PCOffset9));
                        UpdateFlags(DR);
                        break;
                    }
                case (ushort)InstructionSet.OP_LDR:
                    {
                        ushort DR = (ushort)((Instruction >> 9) & 0x7);
                        ushort BaseR = (ushort)((Instruction >> 6) & 0x7);
                        ushort Offset6 = SignExtend((ushort)(Instruction & 0x3F), 6);
                        Registers[DR] = MemoryRead((ushort)(Registers[BaseR] + Offset6));
                        UpdateFlags(DR);
                        break;
                    }
                case (ushort)InstructionSet.OP_LEA:
                    {
                        ushort DR = (ushort)((Instruction >> 9) & 0x7);
                        ushort PCOffset9 = SignExtend((ushort)(Instruction & 0x1FF), 9);
                        Registers[DR] = (ushort)(Registers[(ushort)Register.R_PC] + PCOffset9);
                        UpdateFlags(DR);
                        break;
                    }
                case (ushort)InstructionSet.OP_NOT:
                    {
                        ushort DR = (ushort)((Instruction >> 9) & 0x7);
                        ushort SR = (ushort)((Instruction >> 6) & 0x7);
                        Registers[DR] = (ushort)(~Registers[SR]);
                        UpdateFlags(DR);
                        break;
                    }
                case (ushort)InstructionSet.OP_RTI:
                    {
                        throw new Exception("Bad Opcode: RTI instruction is unused.");
                    }
                case (ushort)InstructionSet.OP_RES:
                    {
                        throw new Exception("Bad Opcode: RES instruction is unused.");
                    }
                case (ushort)InstructionSet.OP_ST:
                    {
                        ushort SR = (ushort)((Instruction >> 9) & 0x7);
                        ushort PCOffset9 = SignExtend((ushort)(Instruction & 0x1FF), 9);
                        MemoryWrite((ushort)(Registers[(ushort)Register.R_PC] + PCOffset9), (ushort)Registers[SR]);
                        break;
                    }
                case (ushort)InstructionSet.OP_STI:
                    {
                        ushort SR = (ushort)((Instruction >> 9) & 0x7);
                        ushort PCOffset9 = SignExtend((ushort)(Instruction & 0x1FF), 9);
                        ushort Address = MemoryRead((ushort)(Registers[(ushort)Register.R_PC] + PCOffset9));
                        MemoryWrite(Address, Registers[SR]);
                        break;
                    }
                case (ushort)InstructionSet.OP_STR:
                    {
                        ushort SR = (ushort)((Instruction >> 9) & 0x7);
                        ushort BaseR = (ushort)((Instruction >> 6) & 0x7);
                        ushort Offset6 = SignExtend((ushort)(Instruction & 0x3F), 6);
                        MemoryWrite((ushort)(Registers[BaseR] + Offset6), Registers[SR]);
                        break;
                    }
                case (ushort)InstructionSet.OP_TRAP:
                    {
                        HandleTrap(Instruction, ref Running);
                        break;
                    }
                default:
                    throw new Exception("Invalid OpCode given.");
            }
        }

        static void Main(string[] Args)
        {
            if (Args.Length < 1)
            {
                Console.WriteLine("lc3-vm [image-file1] ...");
                return;
            }

            foreach (string Arg in Args)
            {
                if (ReadImageFile(Arg) != 1)
                {
                    Console.WriteLine($"Failed to load image: {Arg}");
                    return;
                }
            }

            Registers[(ushort)Register.R_PC] = PC_START;

            ushort Running = 1;
            while (Running > 0)
            {
                ushort Instruction = MemoryRead(Registers[(ushort)Register.R_PC]++);

                PerformOperation(Instruction, ref Running);
            }
        }
    }
}
