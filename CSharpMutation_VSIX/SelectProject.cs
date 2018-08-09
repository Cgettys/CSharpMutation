using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;

namespace CSharpMutation_VSIX
{
    public partial class SelectProject : Form
    {
        public Project SelectedProject;
        public Project SelectedTestProject;
        public SelectProject()
        {
            InitializeComponent();
            comboBox1.Items.AddRange(Projects.ToArray());
            comboBox2.Items.AddRange(Projects.ToArray());
            comboBox1.DisplayMember = "Name";
            comboBox2.DisplayMember = "Name";
            AcceptButton = button1;
        }
        public List<Project> Projects => MutationTesting.Instance.Projects;

        private void button1_Click(object sender, EventArgs e)
        {
            SelectedProject = (Project)comboBox1.SelectedItem;
            SelectedTestProject = (Project) comboBox2.SelectedItem;
            DialogResult = DialogResult.OK;
        }
    }
}
