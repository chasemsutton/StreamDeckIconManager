using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using Add_Stream_Deck_Icon.Properties;
using System.Net.Http;
using System.Drawing;
using System.Drawing.Imaging;

namespace Add_Stream_Deck_Icon
{
    public partial class Form1 : Form
    {
        private List<Manifest> allPacks = new List<Manifest>();
        public List<string> files = new List<string>();
        string streamDeckPacksPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Elgato\\StreamDeck\\IconPacks";
        public string json = "";
        public bool changesMade = false;
        public bool existingPack = false;
        public List<int> existingIcons = new List<int>();
        Manifest importWaiting;
        string importWaitingTempPath;
        public List<Bitmap> imagesToAdd = new List<Bitmap>();
        public Form1()
        {
            InitializeComponent();
            if (!Directory.Exists(streamDeckPacksPath))
            {
                MessageBox.Show("Could not locate Stream Deck icon pack directory");
                Environment.Exit(0);
            }
            string[] dirs = Directory.GetDirectories(streamDeckPacksPath);
            foreach (string dir in dirs)
            {
                if (dir.Substring(dir.LastIndexOf('\\') + 1).StartsWith("zzcustomuser"))
                {
                    Manifest testMani = new Manifest(dir);
                    allPacks.Add(testMani);
                }
            }
            UpdateListBoxText();
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Multiselect = true;
                dialog.Filter = "Image files (*.jpg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    foreach (string fileName in dialog.FileNames)
                    {
                        MemoryStream ms = new MemoryStream(File.ReadAllBytes(fileName));
                        imagesToAdd.Add(new Bitmap(ms));
                    }
                    ListNewIcons();
                    label5.Visible = false;
                }
            }
        }

        private void textBox1_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Link;
            else
                e.Effect = DragDropEffects.None;
        }

        private void textBox1_DragDrop(object sender, DragEventArgs e)
        {
            var tempFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
            List<string> addedFiles = new List<string>();
            foreach (var file in tempFiles)
            {
                if (System.IO.Path.GetExtension(file).Equals(".png", StringComparison.InvariantCultureIgnoreCase) || System.IO.Path.GetExtension(file).Equals(".jpg", StringComparison.InvariantCultureIgnoreCase) || System.IO.Path.GetExtension(file).Equals(".jpeg", StringComparison.InvariantCultureIgnoreCase) || System.IO.Path.GetExtension(file).Equals(".gif", StringComparison.InvariantCultureIgnoreCase))
                {
                    addedFiles.Add(file);
                }
                else
                {
                    MessageBox.Show("Must Be PNG or JPG File Format");
                }
            } // get all files droppeds  
            if (addedFiles != null && addedFiles.Any())
            {
                foreach (string fileName in addedFiles)
                {
                    MemoryStream ms = new MemoryStream(File.ReadAllBytes(fileName));
                    imagesToAdd.Add(new Bitmap(ms));
                }
                ListNewIcons();
                label5.Visible = false;
            }
        }

        private void buttonAddIcons_Click(object sender, EventArgs e)
        {
            if (imagesToAdd.Count > 0)
            {
                if (allPacks.Any())
                {
                    AddIconsVisible(true);
                    AskForIconInfo(imagesToAdd[0]);
                }
                else { MessageBox.Show("Create a pack first"); }
            }
            else MessageBox.Show("You don't have any files selected.", "Error");
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            if (existingIcons.Any())
            {
                GetIconInfo(allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Picture);
                existingIcons.RemoveAt(0);
                if (existingIcons.Any())
                {
                    pictureBox1.Image = allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Picture;
                    textBox3.Text = allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Name;
                    textBox4.Text = String.Join(',',allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Tags);
                    textBox3.Focus();
                }
                else
                {
                    AddIconsVisible(false);
                    existingIcons = new List<int>();
                    StartOver(false);
                    ListExistingIcons();
                }
            }
            else
            {
                GetIconInfo(imagesToAdd[0]);
                imagesToAdd.RemoveAt(0);
                if (imagesToAdd.Count > 0 && imagesToAdd[0] != null)
                {
                    AskForIconInfo(imagesToAdd[0]);
                    textBox3.Focus();
                }
                else
                {
                    AddIconsVisible(false);
                    StartOver(true);
                }
            }

        }

        private void buttonSkip_Click(object sender, EventArgs e)
        {
            textBox3.Clear();
            textBox4.Clear();
            while (imagesToAdd.Count > 0 && imagesToAdd[0] != null)
            {
                GetIconInfo(imagesToAdd[0]);
                imagesToAdd.RemoveAt(0);
            }
            AddIconsVisible(false);
            StartOver(true);
        }

        private void buttonDeleteAll_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to DELETE ALL ICONS from pack:" + allPacks[listBox1.SelectedIndex].Name, "WARNING", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                if (listBox1.SelectedIndex != -1)
                {
                    changesMade = true;
                    foreach (Icon icon in allPacks[listBox1.SelectedIndex].Icons)
                    {
                        icon.Picture.Dispose();
                    }
                    allPacks[listBox1.SelectedIndex].Icons.Clear();
                    ListExistingIcons();
                }
            }
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            ListExistingIcons();
        }

        private void AskForIconInfo(Bitmap picture)
        {
            pictureBox1.Image = picture;
            textBox3.Clear();
            textBox4.Clear();
            textBox3.Focus();
        }

        private void GetIconInfo(Bitmap picture)
        {
            string[] tags = { "custom" };
            string name = "Custom Icon " + (allPacks[listBox1.SelectedIndex].HighestIconNumber + 1).ToString();
            if (!String.IsNullOrEmpty(textBox3.Text))
            {
                name = textBox3.Text;
            }
            if (!String.IsNullOrEmpty(textBox4.Text))
            {
                tags = ("custom," + textBox4.Text).Split(',');
            }
            for (int i = 0; i < tags.Length; i++)
            {
                tags[i].Trim();
            }
            if(!existingIcons.Any()) allPacks[listBox1.SelectedIndex].AddIcon(name, tags, picture);
            else
            {
                allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Name = name;
                allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Tags = tags;
            }
            changesMade = true;
            textBox3.Clear();
            textBox4.Clear();
        }

        private void AddIconsVisible(bool enabled)
        {
            listView2.Visible = !enabled;
            textBox3.Visible = enabled;
            textBox4.Visible = enabled;
            buttonBrowse.Visible = !enabled;
            buttonAddIcons.Visible = !enabled;
            buttonOK.Visible = enabled;
            buttonSkip.Visible = enabled;
            label1.Visible = enabled;
            label2.Visible = enabled;
            label3.Visible = enabled;
            pictureBox1.Visible = enabled;
            DefaultControlsEnabled(!enabled);
        }

        public void StartOver(bool newIcons)
        {
            if(newIcons) imagesToAdd = new List<Bitmap>();
            listView2.Items.Clear();
            textBox3.Clear();
            textBox4.Clear();
            label5.Visible = true;
            if (pictureBox1.Image != null)
            {
                pictureBox1.Image = null;
            }
            ListExistingIcons();
            MessageBox.Show("Done.");
        }
        private void UpdateListBoxText()
        {
            listBox1.Items.Clear();
            if (allPacks.Any())
            {
                foreach (Manifest manifest in allPacks)
                {
                    listBox1.Items.Add(manifest.Name);
                }
                if (listBox1.Items.Count > 0) listBox1.SelectedIndex = 0;
            }
        }
        public void ListExistingIcons()
        {
            listView1.Items.Clear();
            int i = 0;
            listView1.View = View.LargeIcon;
            ImageList imageList = new ImageList();
            imageList.ImageSize = new Size(60, 60);
            foreach (Icon icon in allPacks[listBox1.SelectedIndex].Icons)
            {
                imageList.Images.Add(icon.Picture);

                listView1.Items.Add(new ListViewItem
                {
                    ImageIndex = i,
                    Text = icon.Name,
                    Tag = icon.Tags
                });
                i++;
            }
            listView1.LargeImageList = imageList;
            pictureBox2.Image = allPacks[listBox1.SelectedIndex].Emblem;
        }
        public void ListNewIcons()
        {
            listView2.Items.Clear();
            int i = 0;
            listView2.View = View.LargeIcon;
            ImageList imageList = new ImageList();
            imageList.ImageSize = new Size(60, 60);
            foreach (Image icon in imagesToAdd)
            {
                imageList.Images.Add(icon);

                listView2.Items.Add(new ListViewItem
                {
                    ImageIndex = i,
                    Text = "Import",
                    Tag = "Import"
                });
                i++;
            }
            listView2.LargeImageList = imageList;
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            if (allPacks.Any())
            {
                foreach (Manifest manifest in allPacks)
                {
                    manifest.SaveManifest();
                }
            }
            MessageBox.Show("Done. Restart Stream Deck for changes to take effect!");
            changesMade = false;
            Close();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if(importWaiting != null) importWaiting.DisposeAllImages();
            string[] dirs = Directory.GetDirectories(Path.GetTempPath());
            foreach (string dir in dirs)
            {
                if (dir.Contains(".sdIconPack"))
                {
                    DirectoryInfo di = new DirectoryInfo(dir);
                    di.Delete(true);
                }
            }
                if (changesMade)
            {
                if (MessageBox.Show("Would you like changes saved to stream deck before exiting?", "WARNING", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    foreach (Manifest manifest in allPacks)
                    {
                        manifest.SaveManifest();
                    }
                    MessageBox.Show("Done. Restart Stream Deck for changes to take effect!");
                    Environment.Exit(0);
                }
            }
        }

        private void buttonNewPack_Click(object sender, EventArgs e)
        {
            NewPackControlsVisible(true);
            changesMade = true;
        }

        private void buttonDeletePack_Click(object sender, EventArgs e)
        {
            if (allPacks.Any())
            {
                if (MessageBox.Show("Are you sure you want to delete the entire icon pack named:" + allPacks[listBox1.SelectedIndex].Name + "?", "WARNING", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    changesMade = true;
                    DirectoryInfo di = new DirectoryInfo(allPacks[listBox1.SelectedIndex].IconPackPath);
                    allPacks[listBox1.SelectedIndex].DisposeAllImages();
                    di.Delete(true);
                    allPacks.RemoveAt(listBox1.SelectedIndex);
                    UpdateListBoxText();
                }
            }
            else { MessageBox.Show("Create a pack first!"); }
        }

        private void buttonImport_Click(object sender, EventArgs e)
        {
            changesMade = true;
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "zip archive (*.zip)|*.zip";
            ofd.RestoreDirectory = true;
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                ZipFile.ExtractToDirectory(ofd.FileName, Path.GetTempPath(), true);
            }
            string[] dirs = Directory.GetDirectories(Path.GetTempPath());
            foreach (string dir in dirs)
            {
                if (dir.Contains(".sdIconPack"))
                {
                    bool badName = false;
                    importWaiting = new Manifest(dir);
                    importWaitingTempPath = dir;
                    foreach (Manifest manifest in allPacks)
                    {
                            if (String.Equals(manifest.Name.Replace(" ", "").ToLower(),importWaiting.Name.Replace(" ", "").ToLower()))
                            {
                                BadNameControlsVisible(true);
                                label7.Text = importWaiting.Name;
                                badName = true;
                                MessageBox.Show("Pack Name Already Exists");
                                textBox5.Clear();
                                textBox5.Focus();
                            }
                    }
                    if (!badName)
                    {
                        importWaiting.NewPackPath(streamDeckPacksPath);
                        importWaiting.SaveManifest();
                        Manifest tempMani = new Manifest(importWaiting.IconPackPath);
                        allPacks.Add(tempMani);
                        importWaiting.DisposeAllImages();
                        importWaiting = null;
                        DirectoryInfo di = new DirectoryInfo(importWaitingTempPath);
                        di.Delete(true);
                        importWaitingTempPath = null;
                        UpdateListBoxText();

                    }
                } 
            }
        }
        private void TestNewName()
        {
            bool badName = false;
            importWaiting.ChangePackName(textBox5.Text);
            foreach (Manifest manifest in allPacks)
            {
                if (String.Equals(manifest.Name.Replace(" ", "").ToLower(), importWaiting.Name.Replace(" ", "").ToLower()))
                {
                    BadNameControlsVisible(true);
                    label7.Text = importWaiting.Name;
                    badName = true;
                    MessageBox.Show("Pack Name Already Exists");
                    textBox5.Clear();
                    textBox5.Focus();
                }
            }
            if (!badName)
            {
                textBox5.Clear();
                BadNameControlsVisible(false);
                label7.Text = "";
                importWaiting.NewPackPath(streamDeckPacksPath);
                importWaiting.SaveManifest();
                Manifest tempMani = new Manifest(importWaiting.IconPackPath);
                allPacks.Add(tempMani);
                importWaiting.DisposeAllImages();
                importWaiting = null;
                DirectoryInfo di = new DirectoryInfo(importWaitingTempPath);
                di.Delete(true);
                importWaitingTempPath = null;
                UpdateListBoxText();

            }
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            if (allPacks.Any())
            {
                SaveFileDialog saveFileDialog1 = new SaveFileDialog();
                saveFileDialog1.Filter = "zip archive (*.zip)|*.zip";
                saveFileDialog1.RestoreDirectory = true;

                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    ZipFile.CreateFromDirectory(allPacks[listBox1.SelectedIndex].IconPackPath, saveFileDialog1.FileName, CompressionLevel.Optimal, true);
                }
            } else { MessageBox.Show("Create a pack first!"); }
        }

        private void buttonDeleteSelectedIcons_Click(object sender, EventArgs e)
        {
            if (allPacks.Any())
            {
                changesMade = true;
                var selected = listView1.SelectedIndices;
                foreach (var i in selected)
                {
                    allPacks[listBox1.SelectedIndex].RemoveIcon(Int32.Parse(i.ToString()));
                }
                ListExistingIcons();
            }
            else { MessageBox.Show("Create a pack first!"); }
        }

        private void buttonbadNameOK_Click(object sender, EventArgs e)
        {
            TestNewName();
        }

        //Textbox Keypress Actions
        private void textBox4_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (Char.IsLetterOrDigit(e.KeyChar) == false && !e.KeyChar.Equals(',') && !e.KeyChar.Equals(' '))
            {
                if (e.KeyChar == (char)(Keys.Back))
                {
                    e.Handled = false;
                }
                else
                {
                    e.Handled = true;
                }
            }
        }

        private void textBox3_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (Char.IsLetterOrDigit(e.KeyChar) == false && !e.KeyChar.Equals(' '))
            {
                if (e.KeyChar == (char)(Keys.Back))
                {
                    e.Handled = false;
                }
                else
                {
                    e.Handled = true;
                }
            }
        }

        private void textBox5_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (Char.IsLetterOrDigit(e.KeyChar) == false && !e.KeyChar.Equals(' '))
            {
                if (e.KeyChar == (char)(Keys.Back))
                {
                    e.Handled = false;
                }
                else
                {
                    e.Handled = true;
                }
            }
        }

        private void textBox6_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (Char.IsLetterOrDigit(e.KeyChar) == false && !e.KeyChar.Equals(' '))
            {
                if (e.KeyChar == (char)(Keys.Back))
                {
                    e.Handled = false;
                }
                else
                {
                    e.Handled = true;
                }
            }
        }

        private void textBox7_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (Char.IsLetterOrDigit(e.KeyChar) == false && !e.KeyChar.Equals(' '))
            {
                if (e.KeyChar == (char)(Keys.Back))
                {
                    e.Handled = false;
                }
                else
                {
                    e.Handled = true;
                }
            }
        }

        private void textBox8_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (Char.IsLetter(e.KeyChar) == false && !e.KeyChar.Equals(' '))
            {
                if (e.KeyChar == (char)(Keys.Back))
                {
                    e.Handled = false;
                }
                else
                {
                    e.Handled = true;
                }
            }
        }
        private void DefaultControlsEnabled(bool enabled)
        {
            buttonAddIcons.Enabled = enabled;
            buttonBrowse.Enabled = enabled;
            buttonChangePackEmblem.Enabled = enabled;
            buttonDeleteAll.Enabled = enabled;
            buttonDeletePack.Enabled = enabled;
            buttonDeleteSelectedIcons.Enabled = enabled;
            buttonExport.Enabled = enabled;
            buttonImport.Enabled = enabled;
            buttonNewPack.Enabled = enabled;
            buttonSave.Enabled = enabled;
            listBox1.Enabled = enabled;
            buttonChangeIconName.Enabled = enabled;
            buttonChangePackDetails.Enabled = enabled;
        }
        private void NewPackControlsVisible(bool enabled)
        {
            listView1.Visible = !enabled;
            label9.Visible = enabled;
            label10.Visible = enabled;
            label11.Visible = enabled;
            label12.Visible = enabled;
            label13.Visible = enabled;
            textBox6.Visible = enabled;
            textBox7.Visible = enabled;
            textBox8.Visible = enabled;
            buttonNewPackOK.Visible = enabled;
            buttonCancelNewPack.Visible = enabled;
            if (!enabled)
            {
                textBox6.Clear();
                textBox7.Clear();
                textBox8.Clear();
            }
            DefaultControlsEnabled(!enabled);
        }
        private void BadNameControlsVisible(bool enabled)
        {
            listView1.Visible = !enabled;
            label6.Visible = enabled;
            label7.Visible = enabled;
            label8.Visible = enabled;
            textBox5.Visible = enabled;
            buttonbadNameOK.Visible = enabled;
            DefaultControlsEnabled(!enabled);
        }
        private void buttonCancelNewPack_Click(object sender, EventArgs e)
        {
            NewPackControlsVisible(false);
            existingPack = false;
        }

        private void buttonNewPackOK_Click(object sender, EventArgs e)
        {
            if (textBox6.Text == "")
            {
                MessageBox.Show("You must enter a pack name");
            }
            else
            {
                bool badName = false;
                string name = textBox6.Text;
                if (existingPack && String.Equals(allPacks[listBox1.SelectedIndex].Name.Replace(" ", "").ToLower(), name.Replace(" ", "").ToLower()))
                {
                    allPacks[listBox1.SelectedIndex].Name = name;
                    if (String.IsNullOrWhiteSpace(textBox7.Text)) allPacks[listBox1.SelectedIndex].Description = "Custom icons for Elgato Stream Deck";
                    else allPacks[listBox1.SelectedIndex].Description = textBox7.Text;
                    if (String.IsNullOrWhiteSpace(textBox8.Text)) allPacks[listBox1.SelectedIndex].Author = "Custom User";
                    else allPacks[listBox1.SelectedIndex].Author = textBox8.Text;
                    existingPack = false;
                    textBox6.Clear();
                    textBox7.Clear();
                    textBox8.Clear();
                    NewPackControlsVisible(false);
                    UpdateListBoxText();
                    changesMade = true;
                }
                else
                {
                    foreach (Manifest manifest in allPacks)
                    {
                        if (String.Equals(manifest.Name.Replace(" ", "").ToLower(), name.Replace(" ", "").ToLower()))
                        {
                            badName = true;
                            MessageBox.Show("Pack Name Already Exists");
                            textBox6.Clear();
                            textBox6.Focus();
                        }
                    }
                    if (!badName)
                    {
                        if(existingPack)
                        {
                            allPacks[listBox1.SelectedIndex].Name = name;
                            if (String.IsNullOrWhiteSpace(textBox7.Text)) allPacks[listBox1.SelectedIndex].Description = "Custom icons for Elgato Stream Deck";
                            else allPacks[listBox1.SelectedIndex].Description = textBox7.Text;
                            if (String.IsNullOrWhiteSpace(textBox8.Text)) allPacks[listBox1.SelectedIndex].Author = "Custom User";
                            else allPacks[listBox1.SelectedIndex].Author = textBox8.Text;
                            string tempPath = allPacks[listBox1.SelectedIndex].IconPackPath;
                            allPacks[listBox1.SelectedIndex].NewPackPath(streamDeckPacksPath);
                            allPacks[listBox1.SelectedIndex].SaveManifest();
                            DirectoryInfo di = new DirectoryInfo(tempPath);
                            di.Delete(true);
                            existingPack = false;
                            textBox6.Clear();
                            textBox7.Clear();
                            textBox8.Clear();
                            NewPackControlsVisible(false);
                            UpdateListBoxText();
                            changesMade = true;
                        }
                        else
                        {
                            string description;
                            string author;
                            if (String.IsNullOrWhiteSpace(textBox7.Text)) description = "Custom icons for Elgato Stream Deck";
                            else description = textBox7.Text;
                            if (String.IsNullOrWhiteSpace(textBox8.Text)) author = "Custom User";
                            else author = textBox8.Text;
                            Manifest tempMani = new Manifest(name, description, author, streamDeckPacksPath);
                            tempMani.SaveManifest();
                            allPacks.Add(new Manifest(tempMani.IconPackPath));
                            textBox6.Clear();
                            textBox7.Clear();
                            textBox8.Clear();
                            NewPackControlsVisible(false);
                            UpdateListBoxText();
                            changesMade = true; 
                        }
                    }
                }
            }
        }

        private void buttonChangePackEmblem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                if (allPacks.Any())
                {
                    dialog.Multiselect = false;
                    dialog.Filter = "Image files (*.jpg;*.png)|*.jpg;*.jpeg;*.png";
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        Image picture = Image.FromFile(dialog.FileName);
                        Image bmpNewImage = new Bitmap(picture.Width,
                                           picture.Height);
                        Graphics gfxNewImage = Graphics.FromImage(bmpNewImage);
                        gfxNewImage.DrawImage(picture,
                                              new Rectangle(0, 0, bmpNewImage.Width,
                                                            bmpNewImage.Height),
                                              0, 0,
                                              picture.
                                              Width, picture.Height,
                                              GraphicsUnit.Pixel);
                        gfxNewImage.Dispose();
                        picture.Dispose();
                        allPacks[listBox1.SelectedIndex].Emblem = bmpNewImage;
                        changesMade = true;
                        ListExistingIcons();
                    }
                }
                else { MessageBox.Show("Create a pack first!"); }
            }
        }


        private void buttonChangePackDetails_Click(object sender, EventArgs e)
        {
            if (allPacks.Any())
            {
                NewPackControlsVisible(true);
                textBox6.Text = allPacks[listBox1.SelectedIndex].Name;
                textBox7.Text = allPacks[listBox1.SelectedIndex].Description;
                textBox8.Text = allPacks[listBox1.SelectedIndex].Author;
                existingPack = true;
            }
            
        }

        private void buttonChangeIconName_Click(object sender, EventArgs e)
        {
            if (allPacks.Any())
            {
                var selected = listView1.SelectedIndices;
                foreach (var i in selected)
                {
                    existingIcons.Add(Int32.Parse(i.ToString()));
                }
                if (existingIcons.Any())
                {
                    AddIconsVisible(true);
                    buttonSkip.Visible = false;
                    label5.Visible = false;
                    pictureBox1.Image = allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Picture;
                    textBox3.Text = allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Name;
                    textBox4.Text = String.Join(',', allPacks[listBox1.SelectedIndex].Icons[existingIcons[0]].Tags);
                    textBox3.Focus();
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            MemoryStream ms = new MemoryStream(File.ReadAllBytes(@"C:\Users\chase\OneDrive\Pictures\Stream Deck\Elgato.gif"));
            Bitmap bmpTest = new Bitmap(ms);
            File.Delete(@"C:\Users\chase\OneDrive\Pictures\Stream Deck\Elgato.gif");
            bmpTest.Save(@"C:\Users\chase\OneDrive\Pictures\Stream Deck\ElgatoSave.gif", ImageFormat.Gif);
        }
    }

    class Icon
    {
        public int IconNumber { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string[] Tags { get; set; }
        public Bitmap Picture { get; set; }

        public Icon(int index, string path, string name, string[] tags, Bitmap picture)
        {
            /*Image bmpNewImage = new Bitmap(picture.Width,
                                           picture.Height);
            Graphics gfxNewImage = Graphics.FromImage(bmpNewImage);
            gfxNewImage.DrawImage(picture,
                                  new Rectangle(0, 0, bmpNewImage.Width,
                                                bmpNewImage.Height),
                                  0, 0,
                                  picture.
                                  Width, picture.Height,
                                  GraphicsUnit.Pixel);
            gfxNewImage.Dispose();
            picture.Dispose();*/
            IconNumber = index;
            Path = path;
            Name = name;
            Tags = tags;
            Picture = picture;
        }

        public string AddToIconPack(string json,bool firstIcon)
        {
            string appendTags = "";
            foreach(string tag in Tags)
            {
                appendTags += "\n        \"" + tag + "\",";
            }
            string appendToJson = "\n    {\n      \"path\": \"" + Path + "\",\n      \"name\": \"" + Name + "\",\n      \"tags\": [" + appendTags.Remove(appendTags.Length - 1, 1) + "\n      ]\n    }";
            if(firstIcon) json = json + appendToJson;
            else json = json + "," + appendToJson;
            return json;
        }
    }

    class Manifest
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string IconPackPath { get; set; }
        public int HighestIconNumber { get; set; }
        public Image Emblem { get; set; }

        private string manifestRawData;
        private string iconsRawData;
        
        public List<Icon> Icons { get; set; }

        public Manifest(string iconPackPath)
        {
            Icons = new List<Icon>();
            HighestIconNumber = 0;
            IconPackPath = iconPackPath;
            FetchPackData();
            if (IconPackPath.Substring(IconPackPath.LastIndexOf('\\') + 1).StartsWith("zzcustomuser"))
            {
                try
                {
                    Directory.CreateDirectory(IconPackPath + "\\backup\\icons-old");
                    File.Delete(iconPackPath + "\\backup\\icons-old.json");
                    File.Copy(iconPackPath + "\\icons.json", iconPackPath + "\\backup\\icons-old.json");
                    File.Delete(iconPackPath + "\\backup\\manifest-old.json");
                    File.Copy(iconPackPath + "\\manifest.json", iconPackPath + "\\backup\\manifest-old.json");
                    File.Delete(iconPackPath + "\\backup\\icon-old.png");
                    File.Copy(iconPackPath + "\\icon.png", iconPackPath + "\\backup\\icon-old.png");
                    foreach (string file in Directory.GetFiles(IconPackPath + "\\backup\\icons-old"))
                    {
                        File.Delete(file);
                    }
                    Directory.Delete(iconPackPath + "\\backup\\icons-old");
                    Copy(iconPackPath + "\\icons", iconPackPath + "\\backup\\icons-old");
                }
                catch (Exception ex) { MessageBox.Show("Something went wrong backing up data in: " + IconPackPath + "\n\nrecommend closing program now\n\n" + ex.ToString(), "Error"); }
                try
                {
                    iconsRawData = File.ReadAllText(IconPackPath + "\\icons.json");
                    ImportIconData();
                }
                catch (Exception ex) { MessageBox.Show("Something went wrong fetching icons from: " + IconPackPath + "\n\n" + ex.ToString(), "Error"); }
            }
            
        }

        public Manifest(string name,string description,string author,string streamDeckPacksPath)
        {
            Name = name;
            Description = description;
            Author = author;
            Emblem = Resources.imgIcon1;
            Icons = new List<Icon>();
            HighestIconNumber = 0;
            NewPackPath(streamDeckPacksPath);
        }
        public void NewPackPath(string streamDeckPacksPath)
        {
            IconPackPath = streamDeckPacksPath + "\\zzcustomuser." + Name.Replace(" ","").ToLower() + ".iconpack.sdIconPack";
            Directory.CreateDirectory(IconPackPath);
            Directory.CreateDirectory(IconPackPath + "\\icons");
            Directory.CreateDirectory(IconPackPath + "\\backup\\icons-old");
        }
        public void FetchPackData()
        {
            
            try
            {
                manifestRawData = File.ReadAllText(IconPackPath + "\\manifest.json");
                Author = GetManifestDataPiece(manifestRawData, "Author");
                Name = GetManifestDataPiece(manifestRawData, "Name");
                Description = GetManifestDataPiece(manifestRawData, "Description");
            }catch (Exception ex) { MessageBox.Show("Something went wrong fetching icon pack data from manifest.json in: " + IconPackPath + "\n" + ex.ToString(), "Error"); }
        }

        private string GetManifestDataPiece(string manifestDataRaw, string targetDataPiece)
        {
            string[] dataPieceArray = manifestDataRaw.Split(targetDataPiece + "\": \"");
            string dataPiece = dataPieceArray[1];
            dataPiece = dataPiece.Remove(dataPiece.IndexOf('\"'));
            return dataPiece;
        }

        private string ReplaceManifestDataPiece(string manifestDataRaw, string targetDataPiece, string newData)
        {
            string[] manifestArray = manifestDataRaw.Split(targetDataPiece + "\": \"");
            string manifestSub = manifestArray[1];
            manifestSub = manifestSub.Substring(manifestSub.IndexOf('\"'));
            string newManifest = manifestArray[0] + targetDataPiece + "\": \"" + newData + manifestSub;
            return newManifest;
        }

        public void ChangePackName(string newPackName)
        {
            Name = newPackName;
            manifestRawData = ReplaceManifestDataPiece(manifestRawData, "Name", newPackName);
        }
        public void ChangePackDescription(string newPackDescription)
        {
            Description = newPackDescription;
            manifestRawData = ReplaceManifestDataPiece(manifestRawData, "Description", newPackDescription);
        }

        public void ChangePackAuthor(string author)
        {
            Author = author;
            manifestRawData = ReplaceManifestDataPiece(manifestRawData, "Author", Author);
        }

        private void ImportIconData()
        {
            if(Icons.Any()) Icons.Clear();
            string[] iconDataArray = iconsRawData.Split('{');
            foreach (string iconData in iconDataArray)
            {
                if (iconData.Contains("path") && iconData.Contains("name") && iconData.Contains("tags"))
                {
                    string path = GetIconDataPiece(iconData, "path");
                    int number = Int32.Parse(GetIconDataPiece(iconData, "number"));
                    Icons.Add(new Icon(number, path, GetIconDataPiece(iconData, "name"), GetIconDataPiece(iconData, "tags").Split(','), new Bitmap(new MemoryStream(File.ReadAllBytes(IconPackPath + "\\backup\\icons-old\\" + path))) ));
                    if(number > HighestIconNumber) HighestIconNumber = number;
                }
            }
            if (File.Exists(IconPackPath + "\\backup\\icon-old.png"))
            {
                Image picture = Image.FromFile(IconPackPath + "\\backup\\icon-old.png");
                Image bmpNewImage = new Bitmap(picture.Width,
                                           picture.Height);
                Graphics gfxNewImage = Graphics.FromImage(bmpNewImage);
                gfxNewImage.DrawImage(picture,
                                      new Rectangle(0, 0, bmpNewImage.Width,
                                                    bmpNewImage.Height),
                                      0, 0,
                                      picture.
                                      Width, picture.Height,
                                      GraphicsUnit.Pixel);
                gfxNewImage.Dispose();
                picture.Dispose();
                Emblem = bmpNewImage;
            }
            else Emblem = Resources.imgIcon1;
        }

        public void AddIcon(string name, string[] tags,Bitmap picture)
        {

            string path = "Custom Icon-" + (HighestIconNumber + 1).ToString() + "." + new ImageFormatConverter().ConvertToString(picture.RawFormat).ToLower();
            Icons.Add(new Icon(HighestIconNumber + 1, path, name, tags, picture));
            HighestIconNumber++;
        }
        public void RemoveIcon(int index)
        {
            Icons[index].Picture.Dispose();
            Icons.RemoveAt(index);
        }
        private string GetIconDataPiece(string iconDataRaw, string targetDataPiece)
        {
            if (targetDataPiece == "path" || targetDataPiece == "name")
            {
                string[] dataPieceArray = iconDataRaw.Split(targetDataPiece);
                string dataPiece = dataPieceArray[1].Substring(dataPieceArray[1].IndexOf(':') + 1);
                dataPiece = dataPiece.Substring(dataPiece.IndexOf('\"') + 1);
                dataPiece = dataPiece.Remove(dataPiece.IndexOf('\"'));
                return dataPiece;
            }
            else if (targetDataPiece == "tags")
            {
                string tags = "";
                string[] dataPieceArray = iconDataRaw.Split(targetDataPiece);
                string dataPiece = dataPieceArray[1].Substring(dataPieceArray[1].IndexOf(':') + 1);
                string[] tempTags = dataPiece.Split(',');
                foreach(string tag in tempTags)
                {
                    if (tag.Contains('\"'))
                    {
                        string tag0 = tag.Substring(tag.IndexOf('\"') + 1);
                        tag0 = tag0.Remove(tag0.IndexOf('\"'));
                        tags += tag0 + ",";
                    }
                }
                if(tags.Length > 0) tags = tags.Remove(tags.Length - 1);
                return tags;
            }
            else if(targetDataPiece == "number")
            {
                string[] dataPieceArray = iconDataRaw.Split("Custom Icon-");
                return dataPieceArray[1].Remove(dataPieceArray[1].IndexOf('.'));
            }
            else return "";
        }
        public void SaveManifest()
        {
            System.IO.DirectoryInfo di = new DirectoryInfo(IconPackPath + "\\icons");
            foreach (FileInfo file in di.EnumerateFiles())
            {
                file.Delete();
            }
            File.Delete(IconPackPath + "\\icons.json");
            File.Delete(IconPackPath + "\\manifest.json");
            File.Delete(IconPackPath + "\\icon.png");
            if (Icons.Any()) SaveImagesToIconsFolder();
            Emblem.Save(IconPackPath + "\\icon.png",ImageFormat.Png);
            File.WriteAllText(IconPackPath + "\\icons.json", CompileIconsJSON());
            CompileNewManifest();
            File.WriteAllText(IconPackPath + "\\manifest.json",manifestRawData);
        }
        private void SaveImagesToIconsFolder()
        {
            foreach(Icon icon in Icons)
            {
                icon.Picture.Save(IconPackPath + "\\icons\\" + icon.Path,icon.Picture.RawFormat);
            }
        }
        private string CompileIconsJSON()
        {
            string json = "";
            bool firstIcon = true;
            if (Icons.Any())
            {
                foreach (Icon icon in Icons)
                {
                    json = icon.AddToIconPack(json, firstIcon);
                    firstIcon = false;
                }
            }
            return "[" + json + "\n  ]";
        }

        private void CompileNewManifest()
        {
            manifestRawData =
@"{
    ""Author"": ""Custom User"",
    ""Description"": ""Custom icons for Elgato Stream Deck"",
    ""Name"": ""Custom User Icons"",
    ""URL"": """",
    ""Version"": ""1.0"",
    ""Icon"": ""icon.png"",
    ""Tags"": ""Custom User Icon""
}
";
            ChangePackName(Name);
            ChangePackDescription(Description);
            ChangePackAuthor(Author);
        }
        public static void Copy(string sourceDirectory, string targetDirectory)
        {
            var source = new DirectoryInfo(sourceDirectory);
            var target = new DirectoryInfo(targetDirectory);

            Directory.CreateDirectory(target.FullName);

            // Copy each file into the new directory.
            foreach (FileInfo fi in source.GetFiles())
            {
                fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
            }
        }
        public void DisposeAllImages()
        {
            foreach(var icon in Icons) icon.Picture.Dispose();
            Emblem.Dispose();
        }

    }
}

/*                var manifestText =
@"{
    ""Author"": ""Custom User"",
    ""Description"": ""Custom icons for Elgato Stream Deck"",
    ""Name"": ""Custom User Icons"",
    ""URL"": """",
    ""Version"": ""1.0"",
    ""Icon"": ""icon.png"",
    ""Tags"": ""Custom User Icon""
}
";*/


/*To-Do
 * copy files and change their name to ODBC Icon-00
 * format Icon info to json file formatting
 * replace existing json file with new updated one
 * error handling
 */