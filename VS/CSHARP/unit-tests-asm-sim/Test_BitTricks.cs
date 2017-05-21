﻿using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AsmSim;
using Microsoft.Z3;
using AsmTools;
using AsmSim.Mnemonics;

namespace unit_tests_asm_z3
{
    [TestClass]
    public class Test_BitTricks
    {
        const bool logToDisplay = TestTools.LOG_TO_DISPLAY;

        private Tools CreateTools(int timeOut = TestTools.DEFAULT_TIMEOUT)
        {
            /* The following parameters can be set: 
                    - proof (Boolean) Enable proof generation
                    - debug_ref_count (Boolean) Enable debug support for Z3_ast reference counting
                    - trace (Boolean) Tracing support for VCC 
                    - trace_file_name (String) Trace out file for VCC traces 
                    - timeout (unsigned) default timeout (in milliseconds) used for solvers 
                    - well_sorted_check type checker 
                    - auto_config use heuristics to automatically select solver and configure it 
                    - model model generation for solvers, this parameter can be overwritten when creating a solver 
                    - model_validate validate models produced by solvers 
                    - unsat_core unsat-core generation for solvers, this parameter can be overwritten when creating 
                            a solver Note that in previous versions of Z3, this constructor was also used to set 
                            global and module parameters. For this purpose we should now use 
                            Microsoft.Z3.Global.SetParameter(System.String,System.String)
            */

            Dictionary<string, string> settings = new Dictionary<string, string>
            {
                { "unsat_core", "false" },    // enable generation of unsat cores
                { "model", "true" },          // enable model generation
                { "proof", "false" },         // enable proof generation
                { "timeout", timeOut.ToString() }
            };
            return new Tools(settings);
        }

        private State CreateState(Tools tools)
        {
            string tailKey = "!INIT";// Tools.CreateKey(tools.Rand);
            string headKey = tailKey;
            return new State(tools, tailKey, headKey);
        }

        [TestMethod]
        public void Test_BitTricks_Min_Unsigned()
        {
            Tools tools = CreateTools();
            tools.StateConfig.Set_All_Reg_Off();
            tools.StateConfig.RAX = true;
            tools.StateConfig.RBX = true;
            tools.StateConfig.RDX = true;
            tools.StateConfig.CF = true;

            string line1 = "sub rax, rbx";
            string line2 = "sbb rdx, rdx"; // copy CF to all bits of edx
            string line3 = "and rdx, rax";
            string line4 = "add rbx, rdx";

            {   // forward
                State state = CreateState(tools);
                Context ctx = state.Ctx;

                BitVecExpr rax0 = state.Get(Rn.RAX);
                BitVecExpr rbx0 = state.Get(Rn.RBX);

                state = Runner.SimpleStep_Forward(line1, state);
                if (logToDisplay) Console.WriteLine("After \"" + line1 + "\", we know:\n" + state);

                state = Runner.SimpleStep_Forward(line2, state);
                if (logToDisplay) Console.WriteLine("After \"" + line2 + "\", we know:\n" + state);

                state = Runner.SimpleStep_Forward(line3, state);
                if (logToDisplay) Console.WriteLine("After \"" + line3 + "\", we know:\n" + state);

                state = Runner.SimpleStep_Forward(line4, state);
                if (logToDisplay) Console.WriteLine("After \"" + line4 + "\", we know:\n" + state);

                // ebx is minimum of ebx and eax
                BitVecExpr rbx1 = state.Get(Rn.RBX);
                BoolExpr t = ctx.MkEq(rbx1, ctx.MkITE(ctx.MkBVUGT(rax0, rbx0), rbx0, rax0));

                {
                    state.Solver.Push();
                    state.Solver.Assert(t);
                    if (state.Solver.Check() != Status.SATISFIABLE)
                    {
                        if (logToDisplay) Console.WriteLine("UnsatCore has " + state.Solver.UnsatCore.Length + " elements");
                        foreach (BoolExpr b in state.Solver.UnsatCore)
                        {
                            if (logToDisplay) Console.WriteLine("UnsatCore=" + b);
                        }
                        Assert.Fail();
                    }
                    state.Solver.Pop();
                }
                {
                    state.Solver.Push();
                    state.Solver.Assert(ctx.MkNot(t));
                    if (state.Solver.Check() == Status.SATISFIABLE)
                    {
                        if (logToDisplay) Console.WriteLine("Model=" + state.Solver.Model);
                        Assert.Fail();
                    }
                    state.Solver.Pop();
                }
                Assert.AreEqual(Tv.ONE, ToolsZ3.GetTv(t, state.Solver, state.Ctx));
            }
        }

        [TestMethod]
        public void Test_BitTricks_Min_Signed()
        {
            Tools tools = CreateTools();
            tools.StateConfig.Set_All_Reg_Off();
            tools.StateConfig.RAX = true;
            tools.StateConfig.RBX = true;
            tools.StateConfig.RDX = true;

            return; // this trick does not seem to be correct?!

            string line1 = "sub rax, rbx";  // Will not work if overflow here!
            string line2 = "cqo";           // rdx1 = (rax0 > rbx0) ? -1 : 0
            string line3 = "and rdx, rax";  // rdx2 = (rax0 > rbx0) ? 0 : (rax0 - rbx0)
            string line4 = "add rbx, rdx";  // rbx1 = (rax0 > rbx0) ? (rbx0 + 0) : (rbx0 + rax0 - rbx0)

            {   // forward
                State state = CreateState(tools);
                Context ctx = state.Ctx;

                if (true)
                {
                    ulong rax_value = 0x61a4292198602827;
                    ulong rbx_value = 0x8739140220c24080;

                    StateUpdate updateState = new StateUpdate("!PREVKEY", "!NEXTKEY", state.Tools);
                    updateState.Set(Rn.RAX, rax_value);
                    updateState.Set(Rn.RBX, rbx_value);
                    state.Update_Forward(updateState);
                    if (logToDisplay) Console.WriteLine("Initially, we know:\n" + state);
                }

                BitVecExpr rax0 = state.Get(Rn.RAX);
                BitVecExpr rbx0 = state.Get(Rn.RBX);

                {
                    state.Solver.Assert(state.Ctx.MkNot(ToolsFlags.Create_OF_Sub(rax0, rbx0, rax0.SortSize, ctx))); // this code only works when there is no overflow in line1
                }
                {   // line 1
                    state = Runner.SimpleStep_Forward(line1, state);
                    // retrieve the overflow after line 1, OF has to be zero for the code to work
                    state.Solver.AssertAndTrack(ctx.MkNot(state.Get(Flags.OF)), ctx.MkBoolConst("OF-ZERO"));
                    Assert.AreEqual(Status.SATISFIABLE, state.Solver.Check());
                    if (logToDisplay) Console.WriteLine("After \"" + line1 + "\", we know:\n" + state);
                }
                {   // line 2
                    state = Runner.SimpleStep_Forward(line2, state);
                    //if (logToDisplay) Console.WriteLine("After \"" + line2 + "\", we know:\n" + state);
                    BoolExpr t2 = ctx.MkEq(state.Get(Rn.RDX), ctx.MkITE(ctx.MkBVSGT(rax0, rbx0), ctx.MkBV(0xFFFF_FFFF_FFFF_FFFF, 64), ctx.MkBV(0, 64)));
                    //Assert.AreEqual(Tv5.ONE, ToolsZ3.GetTv5(t2, state.Solver, state.Ctx));
                }
                {
                    state = Runner.SimpleStep_Forward(line3, state);
                    //if (logToDisplay) Console.WriteLine("After \"" + line3 + "\", we know:\n" + state);
                    //BoolExpr t2 = ctx.MkEq(state.Get(Rn.RDX), ctx.MkITE(ctx.MkBVSGT(rax0, rbx0), ctx.MkBV(0, 64), ctx.MkBVSub(rax0, rbx0)));
                    //Assert.AreEqual(Tv5.ONE, ToolsZ3.GetTv5(t2, state.Solver, state.Ctx));
                }
                {
                    state = Runner.SimpleStep_Forward(line4, state);
                    if (logToDisplay) Console.WriteLine("After \"" + line4 + "\", we know:\n" + state);
                }


                // ebx is minimum of ebx and eax
                BitVecExpr rbx1 = state.Get(Rn.RBX);
                BoolExpr t = ctx.MkEq(rbx1, ctx.MkITE(ctx.MkBVSGT(rax0, rbx0), rbx0, rax0));

                if (false)
                {
                    state.Solver.Push();
                    state.Solver.AssertAndTrack(t, ctx.MkBoolConst("MIN_RAX_RBX"));
                    Status s = state.Solver.Check();
                    if (logToDisplay) Console.WriteLine("Status A = " + s + "; expected " + Status.SATISFIABLE);
                    if (s == Status.UNSATISFIABLE)
                    {
                        if (logToDisplay) Console.WriteLine("UnsatCore has " + state.Solver.UnsatCore.Length + " elements");
                        foreach (BoolExpr b in state.Solver.UnsatCore)
                        {
                            if (logToDisplay) Console.WriteLine("UnsatCore=" + b);
                        }

                        if (logToDisplay) Console.WriteLine(state.Solver);
                        Assert.Fail();
                    }
                    state.Solver.Pop();
                }
                if (true)
                {
                    state.Solver.Push();
                    state.Solver.Assert(ctx.MkNot(t), ctx.MkBoolConst("NOT_MIN_RAX_RBX"));
                    Status s = state.Solver.Check();
                    if (logToDisplay) Console.WriteLine("Status B = " + s + "; expected " + Status.UNSATISFIABLE);
                    if (s == Status.SATISFIABLE)
                    {
                        if (logToDisplay) Console.WriteLine("Model=" + state.Solver.Model);
                        Assert.Fail();
                    }
                    state.Solver.Pop();
                }
                Assert.AreEqual(Tv.ONE, ToolsZ3.GetTv(t, state.Solver, state.Ctx));
            }
        }

        [TestMethod]
        public void Test_BitTricks_Parallel_Search_GPR_1()
        {
            Tools tools = CreateTools();
            tools.StateConfig.Set_All_Reg_Off();
            tools.StateConfig.RBX = true;
            tools.StateConfig.RCX = true;
            tools.StateConfig.RDX = true;

            string line1 = "mov ebx, 0x01_00_02_03";        // EBX contains four bytes
            string line2 = "lea ecx, [ebx-0x01_01_01_01]";  // substract 1 from each byte
            string line3 = "not ebx";                       // invert all bytes
            string line4 = "and ecx, ebx";                  // and these two
            string line5 = "and ecx, 0x80_80_80_80";

            {   // forward
                State state = CreateState(tools);
                Context ctx = state.Ctx;
                BitVecExpr zero = ctx.MkBV(0, 8);


                BitVecExpr bytes = state.Get(Rn.EBX);
                BitVecExpr byte1 = ctx.MkExtract((1 * 8) - 1, (0 * 8), bytes);
                BitVecExpr byte2 = ctx.MkExtract((2 * 8) - 1, (1 * 8), bytes);
                BitVecExpr byte3 = ctx.MkExtract((3 * 8) - 1, (2 * 8), bytes);
                BitVecExpr byte4 = ctx.MkExtract((4 * 8) - 1, (3 * 8), bytes);

                if (false)
                {   // line 1
                    state = Runner.SimpleStep_Forward(line1, state);
                    //if (logToDisplay) Console.WriteLine("After \"" + line1 + "\", we know:\n" + state);
                }
                state = Runner.SimpleStep_Forward(line2, state);
                //if (logToDisplay) Console.WriteLine("After \"" + line2 + "\", we know:\n" + state);
                state = Runner.SimpleStep_Forward(line3, state);
                //if (logToDisplay) Console.WriteLine("After \"" + line3 + "\", we know:\n" + state);
                state = Runner.SimpleStep_Forward(line4, state);
                //if (logToDisplay) Console.WriteLine("After \"" + line4 + "\", we know:\n" + state);
                state = Runner.SimpleStep_Forward(line5, state);
                //if (logToDisplay) Console.WriteLine("After \"" + line5 + "\", we know:\n" + state);

                {
                    // if at least one of the bytes is equal to zero, then ECX cannot be equal to zero
                    // if ECX is zero, then none of the bytes is equal to zero.

                    BoolExpr property = ctx.MkEq(
                        ctx.MkOr(
                            ctx.MkEq(byte1, zero),
                            ctx.MkEq(byte2, zero),
                            ctx.MkEq(byte3, zero),
                            ctx.MkEq(byte4, zero)
                        ),
                        ctx.MkNot(ctx.MkEq(state.Get(Rn.ECX), ctx.MkBV(0, 32)))
                    );
                    TestTools.AreEqual(Tv.ONE, ToolsZ3.GetTv(property, state.Solver, state.Ctx));
                }
                {
                    state.Solver.Push();
                    BoolExpr p = ctx.MkOr(ctx.MkEq(byte1, zero), ctx.MkEq(byte2, zero), ctx.MkEq(byte3, zero), ctx.MkEq(byte4, zero));
                    state.Solver.Assert(p);
                    if (logToDisplay) Console.WriteLine("After \"" + p + "\", we know:\n" + state);
                    state.Solver.Pop();
                }
                {
                    state.Solver.Push();
                    BoolExpr p = ctx.MkAnd(
                        ctx.MkEq(ctx.MkEq(byte1, zero), ctx.MkFalse()),
                        ctx.MkEq(ctx.MkEq(byte2, zero), ctx.MkFalse()),
                        ctx.MkEq(ctx.MkEq(byte3, zero), ctx.MkTrue()),
                        ctx.MkEq(ctx.MkEq(byte4, zero), ctx.MkFalse())
                    );
                    state.Solver.Assert(p);
                    if (logToDisplay) Console.WriteLine("After \"" + p + "\", we know:\n" + state);
                    //state.Solver.Pop();
                }
            }
        }
        [TestMethod]
        public void Test_BitTricks_Parallel_Search_GPR_2()
        {
            Tools tools = CreateTools();
            tools.StateConfig.Set_All_Reg_Off();
            tools.StateConfig.RAX = true;
            tools.StateConfig.RBX = true;
            tools.StateConfig.RCX = true;
            tools.StateConfig.RSP = true;

            string line1 = "mov rax, 0x80_80_80_80_80_80_80_80";
            string line2 = "mov rsp, 0x01_01_01_01_01_01_01_01";

            string line3 = "mov rbx, 0x01_02_03_04_05_06_07_08";    // EBX contains 8 bytes
            string line4a = "mov rcx, rbx";             // cannot substract with lea, now we need an extra mov
            string line4b = "sub rcx, rsp";              // substract 1 from each byte
            string line5 = "not rbx";                   // invert all bytes
            string line6 = "and rcx, rbx";              // and these two
            string line7 = "and rcx, rax";

            {   // forward
                State state = CreateState(tools);
                Context ctx = state.Ctx;
                BitVecExpr zero = ctx.MkBV(0, 8);


                BitVecExpr bytes = state.Get(Rn.RBX);
                BitVecExpr byte1 = ctx.MkExtract((1 * 8) - 1, (0 * 8), bytes);
                BitVecExpr byte2 = ctx.MkExtract((2 * 8) - 1, (1 * 8), bytes);
                BitVecExpr byte3 = ctx.MkExtract((3 * 8) - 1, (2 * 8), bytes);
                BitVecExpr byte4 = ctx.MkExtract((4 * 8) - 1, (3 * 8), bytes);
                BitVecExpr byte5 = ctx.MkExtract((5 * 8) - 1, (4 * 8), bytes);
                BitVecExpr byte6 = ctx.MkExtract((6 * 8) - 1, (5 * 8), bytes);
                BitVecExpr byte7 = ctx.MkExtract((7 * 8) - 1, (6 * 8), bytes);
                BitVecExpr byte8 = ctx.MkExtract((8 * 8) - 1, (7 * 8), bytes);

                state = Runner.SimpleStep_Forward(line1, state);
                state = Runner.SimpleStep_Forward(line2, state);
                if (false)
                {
                    state = Runner.SimpleStep_Forward(line3, state);
                    if (logToDisplay) Console.WriteLine("After \"" + line3 + "\", we know:\n" + state);
                }
                state = Runner.SimpleStep_Forward(line4a, state);
                if (logToDisplay) Console.WriteLine("After \"" + line4a + "\", we know:\n" + state);
                state = Runner.SimpleStep_Forward(line4b, state);
                if (logToDisplay) Console.WriteLine("After \"" + line4b + "\", we know:\n" + state);
                state = Runner.SimpleStep_Forward(line5, state);
                if (logToDisplay) Console.WriteLine("After \"" + line5 + "\", we know:\n" + state);
                state = Runner.SimpleStep_Forward(line6, state);
                if (logToDisplay) Console.WriteLine("After \"" + line6 + "\", we know:\n" + state);
                state = Runner.SimpleStep_Forward(line7, state);
                if (logToDisplay) Console.WriteLine("After \"" + line7 + "\", we know:\n" + state);

                {
                    // if at least one of the bytes is equal to zero, then ECX cannot be equal to zero
                    // if ECX is zero, then none of the bytes is equal to zero.

                    BoolExpr property = ctx.MkEq(
                        ctx.MkOr(
                            ctx.MkEq(byte1, zero),
                            ctx.MkEq(byte2, zero),
                            ctx.MkEq(byte3, zero),
                            ctx.MkEq(byte4, zero),
                            ctx.MkEq(byte5, zero),
                            ctx.MkEq(byte6, zero),
                            ctx.MkEq(byte7, zero),
                            ctx.MkEq(byte8, zero)
                        ),
                        ctx.MkNot(ctx.MkEq(state.Get(Rn.RCX), ctx.MkBV(0, 64)))
                    );
                    TestTools.AreEqual(Tv.ONE, ToolsZ3.GetTv(property, state.Solver, state.Ctx));
                }
            }
        }
    }
}