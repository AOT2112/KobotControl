using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KOBOT_Control
{
    public partial class MainForm : Form
    {
        KobotContol control;
        public MainForm()
        {
            InitializeComponent();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                control.Connect = checkBox1.Checked;
            }
            catch (Exception ex) 
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            OnStateChanged(null, KobotContol.KobotState.NotConnected);
            try
            {
                control = new KobotContol();
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.Message, "FATAL ERROR!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1000);
            }
            timer1.Enabled = true;
            control.StateChanged += OnStateChanged;
        }
        private void OnStateChanged(object sender, KobotContol.KobotState state)
        {
            label2.Invoke(new Action(() => {
                switch (state)
                {
                    case KobotContol.KobotState.NotConnected:
                        button1.Enabled = false;
                        button2.Enabled = false;
                        break;
                    case KobotContol.KobotState.Initializing:
                        button1.Enabled = false;
                        button2.Enabled = false;
                        break;
                    case KobotContol.KobotState.NeedPrepare:
                        button1.Enabled = true;
                        button1.Text = "Prepare";
                        button2.Enabled = true;
                        break;
                    case KobotContol.KobotState.Preparing:
                        button1.Enabled = false;
                        button2.Enabled = true;
                        break;
                    case KobotContol.KobotState.Ready:
                        button1.Enabled = true;
                        button1.Text = "Start";
                        button2.Enabled = true;
                        break;
                    case KobotContol.KobotState.Proccessing:
                        button1.Enabled = false;
                        button2.Enabled = true;
                        break;
                }
                label2.Text = state.ToString();
            }));
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            var df = control.GetFeedback();
            KobotContol.DriveFeedback f;
            bool not_founded;
            for (int i = 0; i < df.Count; i++)
            {
                not_founded = true;
                f = df[i];
                for (int j = 0; j < dataGridView1.Rows.Count; j++)
                {
                    if (f.DriveName.Equals(dataGridView1.Rows[j].Cells[0].Value))
                    {
                        dataGridView1.Rows[j].Cells[1].Value = f.CurrentPosition;
                        dataGridView1.Rows[j].Cells[2].Value = f.TargetPosition;
                        dataGridView1.Rows[j].Cells[3].Value = f.DesiredPosition;
                        not_founded = false;
                        break;
                    }
                }
                if (not_founded)
                {
                    dataGridView1.Rows.Add(f.DriveName, f.CurrentPosition, f.TargetPosition, f.DesiredPosition);
                    dataGridView1.Sort(dataGridView1.Columns[0], ListSortDirection.Ascending);
                }
            }
            label3.Text = control.GetCurrentCommandIndex.ToString();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                //control.Start();
                if (control.GetState == KobotContol.KobotState.NeedPrepare)
                    control.Prepare();
                else
                    control.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                control.Stop();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                control.EmergencyStop();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                control.Close();
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.Message);
                Application.Exit();
            }
        }
    }
}
