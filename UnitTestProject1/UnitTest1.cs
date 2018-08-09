using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        FakeApplication.Program program;
        [TestInitialize]
        public void setUp()
        {
            program = new FakeApplication.Program();
        }

        [TestMethod]
        public void TestMethod1()
        {
            //Assert.Fail("Not implemented");
            Console.WriteLine("Test code executing!");
        }

        [TestMethod]
        public void TestMethod2()
        {
            Assert.AreEqual(2, program.getMeows(2));
        }

        [TestMethod]
        public void TestMethod3()
        {
            try
            {
                program.getMeows(0);
            }
            catch (Exception e)
            {
                return;
            }

            Assert.Fail("should have errored");
        }
    }
}
