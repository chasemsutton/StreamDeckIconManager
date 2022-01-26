using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using Add_Stream_Deck_Icon.Properties;
using System.Net.Http;

namespace Add_Stream_Deck_Icon
{
    public partial class Form1 : Form
    {
        private List<Manifest> allPacks = new List<Manifest>();
        public List<string> files = new List<string>();
        string streamDeckPacksPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Elgato\\StreamDeck\\IconPacks";
        public string json = "";
        public bool changesMade = false;
        Manifest importWaiting;
        string importWaitingTempPath;
        public List<Image> imagesToAdd = new List<Image>();
        public Form1()
        {
            InitializeComponent();
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
            //ListExistingIcons();
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Multiselect = true;
                dialog.Filter = "PNG files (*.png)|*.png";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    foreach (string fileName in dialog.FileNames)
                    {
                        imagesToAdd.Add(Image.FromFile(fileName));
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
                if (System.IO.Path.GetExtension(file).Equals(".png", StringComparison.InvariantCultureIgnoreCase))
                {
                    addedFiles.Add(file);
                }
                else
                {
                    MessageBox.Show("Must Be PNG File Format");
                }
            } // get all files droppeds  
            if (addedFiles != null && addedFiles.Any())
            {
                foreach (string fileName in addedFiles)
                {
                    imagesToAdd.Add(Image.FromFile(fileName));
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
                    ToggleVisibility();
                    AskForIconInfo(imagesToAdd[0]);
                }
                else { MessageBox.Show("Create a pack first"); }
            }
            else MessageBox.Show("You don't have any files selected.", "Error");
        }

        private void buttonOK_Click(object sender, EventArgs e)
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
                ToggleVisibility();
                StartOver();
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
            ToggleVisibility();
            StartOver();
        }

        private void buttonDeleteAll_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to DELETE ALL ICONS from pack:" + allPacks[listBox1.SelectedIndex].Name, "WARNING", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                changesMade = true;
                allPacks[listBox1.SelectedIndex].Icons.Clear();
                ListExistingIcons();
            }
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            ListExistingIcons();
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void AskForIconInfo(Image picture)
        {
            pictureBox1.Image = picture;
            textBox3.Clear();
            textBox4.Clear();
            textBox3.Focus();
        }

        private void GetIconInfo(Image picture)
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
            allPacks[listBox1.SelectedIndex].AddIcon(name, tags, picture);
            changesMade = true;
            textBox3.Clear();
            textBox4.Clear();
        }

        private void ToggleVisibility()
        {
            listView2.Visible = !listView2.Visible;
            textBox3.Visible = !textBox3.Visible;
            textBox4.Visible = !textBox4.Visible;
            buttonBrowse.Visible = !buttonBrowse.Visible;
            buttonAddIcons.Visible = !buttonAddIcons.Visible;
            buttonOK.Visible = !buttonOK.Visible;
            buttonSkip.Visible = !buttonSkip.Visible;
            buttonDeleteAll.Visible = !buttonDeleteAll.Visible;
            label1.Visible = !label1.Visible;
            label2.Visible = !label2.Visible;
            label3.Visible = !label3.Visible;
            label4.Visible = !label4.Visible;
            pictureBox1.Visible = !pictureBox1.Visible;
        }

        public void StartOver()
        {
            imagesToAdd = new List<Image>();
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
            foreach (Manifest manifest in allPacks)
            {
                listBox1.Items.Add(manifest.Name);
            }
            if (listBox1.Items.Count > 0) listBox1.SelectedIndex = 0;
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
            foreach(Manifest manifest in allPacks)
            {
                manifest.SaveManifest();
            }
            Environment.Exit(0);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
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
                        DirectoryInfo di = new DirectoryInfo(importWaitingTempPath);
                        di.Delete(true);
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
                DirectoryInfo di = new DirectoryInfo(importWaitingTempPath);
                di.Delete(true);
                UpdateListBoxText();

            }
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog1 = new SaveFileDialog();
            saveFileDialog1.Filter = "zip archive (*.zip)|*.zip";
            saveFileDialog1.RestoreDirectory = true;

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                    ZipFile.CreateFromDirectory(allPacks[listBox1.SelectedIndex].IconPackPath, saveFileDialog1.FileName,CompressionLevel.Optimal,true);
            }
        }

        private void buttonDeleteSelectedIcons_Click(object sender, EventArgs e)
        {
            changesMade = true;
            var selected = listView1.SelectedIndices;
            foreach (var i in selected)
            {
                allPacks[listBox1.SelectedIndex].RemoveIcon(Int32.Parse(i.ToString()));
            }
            ListExistingIcons();
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
                    string description;
                    string author;
                    if (String.IsNullOrWhiteSpace(textBox7.Text)) description = "Custom icons for Elgato Stream Deck";
                    else description = textBox7.Text;
                    if(String.IsNullOrWhiteSpace((string)textBox8.Text)) author = "Custom User";
                    else author = textBox8.Text;
                    Manifest tempMani = new Manifest(name, description, author, streamDeckPacksPath);
                    tempMani.SaveManifest();
                    allPacks.Add(new Manifest(tempMani.IconPackPath));
                    textBox6.Clear();
                    textBox7.Clear();
                    textBox8.Clear();
                    NewPackControlsVisible(false);
                    UpdateListBoxText();
                }
            }
        }

        private void buttonChangePackEmblem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Multiselect = false;
                dialog.Filter = "PNG files (*.png)|*.png";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    allPacks[listBox1.SelectedIndex].Emblem = Image.FromFile(dialog.FileName);
                    changesMade = true;
                    ListExistingIcons();
                }
            }
        }

        private void buttonClose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }

    class Icon
    {
        public int IconNumber { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string[] Tags { get; set; }
        public Image Picture { get; set; }

        public Icon(int index, string path, string name, string[] tags, Image picture)
        {
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
            Directory.CreateDirectory(IconPackPath + "\\backup\\icons-old");
        }
        public void NewPackPath(string streamDeckPacksPath)
        {
            IconPackPath = streamDeckPacksPath + "\\zzcustomuser." + Name.Replace(" ","").ToLower() + ".iconpack.sdIconPack";
            Directory.CreateDirectory(IconPackPath);
            Directory.CreateDirectory(IconPackPath + "\\icons");
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
            string[] dataPieceArray = manifestDataRaw.Split(targetDataPiece);
            string dataPiece = dataPieceArray[1].Substring(dataPieceArray[1].IndexOf(':') + 1);
            dataPiece = dataPiece.Substring(dataPiece.IndexOf('\"') + 1);
            dataPiece = dataPiece.Remove(dataPiece.IndexOf('\"'));
            return dataPiece;
        }

        private string ReplaceManifestDataPiece(string manifestDataRaw, string targetDataPiece, string newData)
        {
            string[] manifestArray = manifestDataRaw.Split(targetDataPiece);
            string manifestSub = manifestArray[1].Substring(manifestArray[1].IndexOf(':') + 1);
            manifestSub = manifestSub.Substring(manifestSub.IndexOf('\"') + 1);
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
                    Icons.Add(new Icon(number, path, GetIconDataPiece(iconData, "name"), GetIconDataPiece(iconData, "tags").Split(','), Image.FromFile(IconPackPath + "\\backup\\icons-old\\" + path)));
                    if(number > HighestIconNumber) HighestIconNumber = number;
                }
            }
            if (File.Exists(IconPackPath + "\\backup\\icon-old.png")) Emblem = Image.FromFile(IconPackPath + "\\backup\\icon-old.png");
            else Emblem = Resources.imgIcon1;
        }

        public void AddIcon(string name, string[] tags,Image picture)
        {
            
            string path = "Custom Icon-" + (HighestIconNumber + 1).ToString() + ".png";
            Icons.Add(new Icon(HighestIconNumber + 1, path, name, tags, picture));
            HighestIconNumber++;
        }
        public void RemoveIcon(int index)
        {
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
            Emblem.Save(IconPackPath + "\\icon.png");
            File.WriteAllText(IconPackPath + "\\icons.json", CompileIconsJSON());
            CompileNewManifest();
            File.WriteAllText(IconPackPath + "\\manifest.json",manifestRawData);
            DisposeAllImages();
        }
        private void SaveImagesToIconsFolder()
        {
            foreach(Icon icon in Icons)
            {
                icon.Picture.Save(IconPackPath + "\\icons\\" + icon.Path);
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