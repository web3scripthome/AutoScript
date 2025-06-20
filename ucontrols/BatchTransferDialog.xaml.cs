using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using web3script.Models;
using web3script.Services;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Nethereum.Hex.HexTypes;
using Nethereum.Util;
using NBitcoin;
using Task = System.Threading.Tasks.Task;
using Nethereum.RPC.Eth.DTOs;
using System.Numerics;
using System.Collections.ObjectModel;
using web3script.ConScript; // 添加引用MonadStaking
namespace web3script.ucontrols
{
    /// <summary>
    /// BatchTransferDialog.xaml 的交互逻辑
    /// </summary>
    public partial class BatchTransferDialog : Window
    {
        private WalletService _walletService;
        private ProjectService _projectService;
        private List<Wallet> _targetWallets = new List<Wallet>();
        private Account _masterAccount;
        private bool _isTransferring = false;
        private decimal _transferAmount = 0;
        private List<TransferResult> _transferResults = new List<TransferResult>();
        private string _currentCurrencyUnit = " ";
        public string rpcUrl = "https://testnet-rpc.monad.xyz";
        
        // 添加钱包余额列表
        private ObservableCollection<WalletBalanceItem> _walletBalanceItems;
        
        // 用于更新UI的委托
        private delegate void UpdateUIDelegate(string message);
        private delegate void UpdateProgressDelegate(int current, int total);
        private delegate void TransferCompletedDelegate(List<TransferResult> results);
        
        public BatchTransferDialog(WalletService walletService)
        {
            InitializeComponent();
            
            _walletService = walletService;
            _projectService = new ProjectService();
            
            // 加载分组
            LoadGroups();
            
            // 加载项目
            LoadProjects();
            
            // 初始化钱包余额列表
            _walletBalanceItems = new ObservableCollection<WalletBalanceItem>();
            walletBalanceList.ItemsSource = _walletBalanceItems;
            
            // 初始化币种单位和关联文本框变更事件
            // 注意：在LoadProjects中已添加Project_SelectionChanged事件
            txtAmount.TextChanged += txtAmount_TextChanged;
            
            // 首次初始化币种单位
            Dispatcher.InvokeAsync(() => {
                UpdateCurrencyUnit();
            });
        }
        
        private void LoadGroups()
        {
            try
            {
                cmbTargetGroup.Items.Clear();
                
                foreach (var group in _walletService.GetGroups())
                {
                    int walletCount = _walletService.GetWalletsInGroup(group.Id).Count;
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = $"{group.Name} ({walletCount})",
                        Tag = group.Id
                    };
                    cmbTargetGroup.Items.Add(item);
                }
                
                if (cmbTargetGroup.Items.Count > 0)
                {
                    cmbTargetGroup.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载钱包分组失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadProjects()
        {
            try
            {
                cmbProject.Items.Clear();
                
                foreach (var project in _projectService.GetProjects())
                {
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content = project.Name,
                        Tag = project.Id
                    };
                    cmbProject.Items.Add(item);
                }
                
                if (cmbProject.Items.Count > 0)
                {
                    cmbProject.SelectedIndex = 0;
                }
                
                // 添加项目选择变更事件
                cmbProject.SelectionChanged += Project_SelectionChanged;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void TargetGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateWalletCount();
            UpdateTotalAmount();
        }
        
        private void UpdateWalletCount()
        {
            if (cmbTargetGroup.SelectedItem == null) return;
            
            string groupId = (cmbTargetGroup.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(groupId)) return;
            
            _targetWallets = _walletService.GetWalletsInGroup(groupId);
            lblWalletCount.Text = _targetWallets.Count.ToString();
        }
        
        private void UpdateTotalAmount()
        {
            if (!decimal.TryParse(txtAmount.Text, out _transferAmount))
            {
                _transferAmount = 0;
            }
            
            decimal totalAmount = _transferAmount * _targetWallets.Count;
            lblTotalAmount.Text = $"{totalAmount:F6} {_currentCurrencyUnit}";
            
            // 更新gas预估
           
        }
        
        private async Task UpdateGasEstimateAsync()
        {
            try
            {
                if (_targetWallets.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        lblGasEstimate.Text = "0 ETH";
                    });
                    return;
                }
                
                // 预估每笔交易的gas为21000，gas价格为50 Gwei
                BigInteger gasPerTx = new BigInteger(21000);
                
                // 确保在后台线程执行网络操作
                Account account = new Nethereum.Web3.Accounts.Account(_privatekey);
                var web3 = new Web3(account, rpcUrl);

                BigInteger gasPriceWei = await web3.Eth.GasPrice.SendRequestAsync(); // 单位是 Wei
                BigInteger gasLimit = 21000; // 示例 Gas 估算（普通转账）

                BigInteger totalGasWei = gasPriceWei * gasLimit; // 总的 Wei 花费
                decimal totalGasEth = UnitConversion.Convert.FromWei(totalGasWei);

                var AllGas = totalGasEth * _targetWallets.Count;
                
                // 在UI线程更新UI
                await Dispatcher.InvokeAsync(() =>
                {
                    lblGasEstimate.Text = $"约 {AllGas:F6} ETH";
                });
            }
            catch (Exception ex)
            {
                // 在UI线程处理异常
                await Dispatcher.InvokeAsync(() =>
                {
                    lblGasEstimate.Text = "估算失败";
                    AppendLog($"Gas估算失败: {ex.Message}");
                });
            }
        }
        
        private void Amount_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // 只允许输入数字和小数点
            Regex regex = new Regex("[^0-9.]+");
            e.Handled = regex.IsMatch(e.Text);
            
            // 确保只有一个小数点
            if (e.Text == "." && ((TextBox)sender).Text.Contains("."))
            {
                e.Handled = true;
            }
        }
        
        private void txtAmount_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTotalAmount();
        } 
        
        public string _privatekey = "";
        private void ValidateWallet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string keyInput = txtMasterKey.Password;
                
                if (string.IsNullOrEmpty(keyInput))
                {
                    MessageBox.Show("请输入主钱包的助记词或私钥", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // 尝试解析助记词或私钥
                _masterAccount = null;
                string address = null;
                
                // 检查是否是助记词
                if (IsMnemonic(keyInput))
                {
                    var mnemonic = new Mnemonic(keyInput);
                    var hdRoot = mnemonic.DeriveExtKey();
                    var extKey = hdRoot.Derive(new NBitcoin.KeyPath("m/44'/60'/0'/0/0")); 
                    var privateKey = new Nethereum.Signer.EthECKey(extKey.PrivateKey.ToBytes(), true); 
                    _masterAccount = new Account(privateKey);
                    _privatekey = privateKey.GetPrivateKey();
                    address = _masterAccount.Address;
                }
                // 检查是否是私钥
                else if (IsPrivateKey(keyInput))
                {
                    // 确保私钥格式正确
                    if (!keyInput.StartsWith("0x"))
                    {
                        keyInput = "0x" + keyInput;
                    }
                    _privatekey = keyInput;
                    _masterAccount = new Account(keyInput);
                    address = _masterAccount.Address;
                }
                else
                {
                    MessageBox.Show("无效的助记词或私钥格式", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // 显示地址
                txtMasterAddress.Text = address;
                
                // 验证成功，启用转账按钮
                btnTransfer.IsEnabled = true;
                
                // 添加到日志
                AppendLog($"主钱包验证成功，地址: {address}");
                UpdateTotalAmount();
                UpdateGasEstimateAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"验证钱包失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                btnTransfer.IsEnabled = false;
                txtMasterAddress.Text = string.Empty;
            }
        }
        
        private bool IsMnemonic(string input)
        {
            string[] words = input.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            return (words.Length == 12 || words.Length == 15 || words.Length == 18 || 
                    words.Length == 21 || words.Length == 24);
        }
        
        private bool IsPrivateKey(string input)
        {
            // 移除可能的0x前缀
            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                input = input.Substring(2);
            }
            
            // 检查是否是64个十六进制字符
            return input.Length == 64 && input.All(c => "0123456789abcdefABCDEF".Contains(c));
        }
        
        private void StartTransfer_Click(object sender, RoutedEventArgs e)
        {
            if (_isTransferring)
            {
                MessageBox.Show("已有转账正在进行，请等待完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (_masterAccount == null)
            {
                MessageBox.Show("请先验证主钱包", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (_targetWallets.Count == 0)
            {
                MessageBox.Show("所选分组没有钱包", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (!decimal.TryParse(txtAmount.Text, out _transferAmount) || _transferAmount <= 0)
            {
                MessageBox.Show("请输入有效的转账金额", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // 计算所需的总金额（包括转账金额和预估的gas费）
            decimal gasPerTx = 21000m;
            decimal gasPriceGwei = 50m;
            decimal totalGasEth = (_targetWallets.Count * gasPerTx * gasPriceGwei) / 1_000_000_000m;
            decimal totalAmount = (_transferAmount * _targetWallets.Count) + totalGasEth;
            
            // 确认转账
            if (MessageBox.Show(
                $"确认要从主钱包向 {_targetWallets.Count} 个钱包转账吗？\n\n" +
                $"每个钱包: {_transferAmount:F6} {_currentCurrencyUnit}\n" +
                $"估计Gas费: {totalGasEth:F6}\n" +
                $"总计需要: {totalAmount:F6} {_currentCurrencyUnit} + Gas",
                "确认转账",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
            
            // 开始转账
            StartTransferProcess();
        }
        
        private async void StartTransferProcess()
        {
            try
            {
                _isTransferring = true;
                
                // 设置UI状态
                btnValidate.IsEnabled = false;
                btnTransfer.IsEnabled = false;
                btnCancel.Content = "关闭";
                cmbTargetGroup.IsEnabled = false;
                txtAmount.IsEnabled = false;
                cmbProject.IsEnabled = false;
                txtMasterKey.IsEnabled = false;
                
                // 清空日志
                txtTransferLog.Clear();
                _transferResults.Clear();
                
                // 设置进度条初始状态
                transferProgress.Value = 0;
                lblProgressPercent.Text = "0%";
                lblCurrentOperation.Text = "准备转账...";
                
                // 开始转账任务
                await Task.Run(() => TransferTask());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动转账过程失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _isTransferring = false;
                
                // 恢复UI状态
                btnValidate.IsEnabled = true;
                btnTransfer.IsEnabled = true;
                btnCancel.Content = "取消";
                cmbTargetGroup.IsEnabled = true;
                txtAmount.IsEnabled = true;
                cmbProject.IsEnabled = true;
                txtMasterKey.IsEnabled = true;
            }
        }
        
        private async void TransferTask()
        {
            int total = _targetWallets.Count;
            int current = 0;
            
            // 先清空并初始化钱包余额列表
            await Dispatcher.InvokeAsync(() => {
                InitializeWalletBalanceList();
            });
            
            foreach (var wallet in _targetWallets)
            {
                try
                {
                    current++;
                    
                    // 更新UI显示当前操作的钱包
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateCurrentOperation($"正在向 {wallet.Address} 转账...");
                        // 更新列表状态
                        UpdateWalletStatus(wallet.Address, "处理中");
                    });
                    
                    // 转账逻辑 - 这里将调用您自己实现的转账方法
                    TransferResult result = await TransferToWallet(wallet.Address, _transferAmount);
                    
                    // 添加到结果列表
                    _transferResults.Add(result);
                    
                    // 更新日志和进度 - 确保在UI线程中执行
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AppendLog(
                            $"{(result.IsSuccess ? "成功" : "失败")}: {result.WalletAddress} - {result.Amount} {_currentCurrencyUnit}" +
                            (result.IsSuccess ? "" : $", 错误: {result.ErrorMessage}"));
                        
                        // 更新钱包列表中的状态
                        UpdateWalletStatus(wallet.Address, result.IsSuccess ? "成功" : "失败");
                        
                        UpdateProgress(current, total);
                    });
                    
                    // 模拟交易间隔，避免过快发送交易
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    // 记录失败
                    _transferResults.Add(new TransferResult
                    {
                        WalletAddress = wallet.Address,
                        Amount = _transferAmount,
                        IsSuccess = false,
                        ErrorMessage = ex.Message
                    });
                    
                    // 更新日志和进度 - 确保在UI线程中执行
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AppendLog($"错误: 向 {wallet.Address} 转账时发生异常: {ex.Message}");
                        // 更新钱包列表中的状态
                        UpdateWalletStatus(wallet.Address, "失败");
                        UpdateProgress(current, total);
                    });
                }
            }
            
            // 完成转账 - 确保在UI线程中执行
            await Dispatcher.InvokeAsync(() =>
            {
                TransferCompleted(_transferResults);
            });
        }
        public static BigInteger EthToWei(string amountStr)
        {
            return Web3.Convert.ToWei(amountStr, Nethereum.Util.UnitConversion.EthUnit.Ether);
        }
        private async Task<string> SendMon(string toaddress,string amount)
        {
            Account account = new Nethereum.Web3.Accounts.Account(_privatekey);
            var web3 = new Web3(account, rpcUrl);

            // 获取当前的 Gas 价格
            var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();

            var weiAmount = EthToWei(amount); 

            var transactionReceipt = await web3.TransactionManager
              .SendTransactionAndWaitForReceiptAsync(new TransactionInput
              {
                  From = account.Address,
                  To = toaddress,
                  Value = new HexBigInteger(weiAmount),
                  Gas = new HexBigInteger(21000),
                  GasPrice = gasPrice
              });

            return transactionReceipt.TransactionHash;
        }
        
        private async Task<TransferResult> TransferToWallet(string toAddress, decimal amount)
        {
            // 注意：这是示例实现，实际应用中会由用户提供真实的转账逻辑
            try
            {
                // 获取当前选中的项目
                string projectName = "";
                
                // 在UI线程获取项目名称
                await Dispatcher.InvokeAsync(() => {
                    ComboBoxItem selectedProjectItem = cmbProject.SelectedItem as ComboBoxItem;
                    projectName = selectedProjectItem?.Content?.ToString() ?? string.Empty;
                    
                    // 在UI线程记录日志
                    if (projectName == "Monad") {
                        AppendLog($"使用Monad网络向 {toAddress} 转账 {amount} MON");
                    } else if (projectName == "Ethereum") {
                        AppendLog($"使用以太坊网络向 {toAddress} 转账 {amount} ETH");
                    } else {
                        AppendLog($"使用默认网络向 {toAddress} 转账 {amount} {_currentCurrencyUnit}");
                    }
                });
                
                // 根据不同项目执行不同的转账逻辑
                string transactionHash = string.Empty;
                
                switch (projectName)
                {
                    case "Monad":
                        // Monad项目的转账逻辑
                        transactionHash = await SendMon(toAddress, amount.ToString());
                        // 返回成功结果
                        return new TransferResult
                        {
                            WalletAddress = toAddress,
                            Amount = amount,
                            TransactionHash = transactionHash,
                            IsSuccess = true
                        };
                    
                    case "Ethereum":
                        break;
                    default: 
                        break;
                }

                // 返回成功结果
                return new TransferResult
                {
                    WalletAddress = toAddress,
                    Amount = amount,
                    TransactionHash = transactionHash,
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                // 返回失败结果
                return new TransferResult
                {
                    WalletAddress = toAddress,
                    Amount = amount,
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        private void UpdateCurrentOperation(string message)
        {
            lblCurrentOperation.Text = message;
        }

        //private void AppendLog(string message)
        //{
        //    txtTransferLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        //    txtTransferLog.ScrollToEnd();
        //}
        private async void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            try
            {
                // 检查 Dispatcher 是否可用
                if (Dispatcher == null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                    return;
                }

                // 检查控件是否存在
                if (txtTransferLog == null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                    return;
                }

                // 在UI线程处理异常
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        txtTransferLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                        txtTransferLog.ScrollToEnd();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error appending log: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AppendLog: {ex.Message}");
            }
        }
        private void UpdateProgress(int current, int total)
        {
            double percent = (double)current / total * 100;
            transferProgress.Value = percent;
            lblProgressPercent.Text = $"{percent:F0}%";
        }
        
        private void TransferCompleted(List<TransferResult> results)
        {
            _isTransferring = false;
            
            // 统计成功和失败数量
            int success = results.Count(r => r.IsSuccess);
            int failed = results.Count - success;
            
            // 显示完成消息
            lblCurrentOperation.Text = $"转账完成。成功: {success}, 失败: {failed}";
            
            // 添加到日志
            AppendLog($"转账完成。成功: {success}, 失败: {failed}");
            
            // 恢复UI状态
            btnValidate.IsEnabled = true;
            cmbTargetGroup.IsEnabled = true;
            txtAmount.IsEnabled = true;
            cmbProject.IsEnabled = true;
            txtMasterKey.IsEnabled = true;
            btnCheckBalances.IsEnabled = true; // 启用查询余额按钮
            
            // 根据是否有失败的转账，显示不同的提示
            if (failed > 0)
            {
                MessageBox.Show($"转账完成，有 {failed} 笔交易失败。\n请查看日志获取详细信息。", 
                    "转账结果", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show($"转账完成，全部 {success} 笔交易成功。", 
                    "转账结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isTransferring)
            {
                if (MessageBox.Show("确定要关闭窗口吗？当前转账任务将继续在后台运行。", 
                                   "确认关闭", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            
            DialogResult = false;
            Close();
        }
        
        // 添加项目选择变更事件处理
        private void Project_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCurrencyUnit();
            UpdateTotalAmount();
        }
        
        // 更新币种单位
        private void UpdateCurrencyUnit()
        {
            try 
            {
                if (cmbProject.SelectedItem == null) return;
                
                ComboBoxItem selectedItem = cmbProject.SelectedItem as ComboBoxItem;
                string projectName = selectedItem?.Content?.ToString();
                
                if (string.IsNullOrEmpty(projectName)) return;
                
                // 根据项目名称设置对应的币种单位
                switch (projectName)
                {
                    case "Monad":
                        _currentCurrencyUnit = "MON";
                        break;
                    case "Ethereum":
                        _currentCurrencyUnit = "ETH";
                        break;
                    default:
                        _currentCurrencyUnit = "ETH"; // 默认使用ETH
                        break;
                }
                
                // 更新UI上显示的单位 - 确保在UI线程中执行
                Dispatcher.Invoke(() => 
                {
                    // 更新转账金额单位
                    if (txtAmount != null && txtAmount.Parent is Grid grid)
                    {
                        for (int i = 0; i < grid.Children.Count; i++)
                        {
                            if (grid.Children[i] is TextBlock unitBlock && Grid.GetColumn(unitBlock) == 1)
                            {
                                unitBlock.Text = _currentCurrencyUnit;
                                break;
                            }
                        }
                    }
                    
                    // 更新其他使用币种单位的UI元素
                    UpdateTotalAmount();
                });
            }
            catch (Exception ex)
            {
                // 错误处理
                AppendLog($"更新币种单位失败: {ex.Message}");
            }
        }
        
        // 初始化钱包余额列表
        private void InitializeWalletBalanceList()
        {
            _walletBalanceItems.Clear();
            
            for (int i = 0; i < _targetWallets.Count; i++)
            {
                _walletBalanceItems.Add(new WalletBalanceItem
                {
                    Index = i + 1,
                    Address = _targetWallets[i].Address,
                    TransferAmount = $"{_transferAmount} {_currentCurrencyUnit}",
                    Balance = "待查询",
                    Status = "等待中"
                });
            }
        }
        
        // 更新钱包状态
        private void UpdateWalletStatus(string address, string status)
        {
            var item = _walletBalanceItems.FirstOrDefault(w => w.Address == address);
            if (item != null)
            {
                item.Status = status;
                
                // 强制刷新列表视图
                walletBalanceList.Items.Refresh();
            }
        }
        
        // 更新钱包余额
        private void UpdateWalletBalance(string address, string balance)
        {
            var item = _walletBalanceItems.FirstOrDefault(w => w.Address == address);
            if (item != null)
            {
                item.Balance = balance;
                
                // 强制刷新列表视图
                walletBalanceList.Items.Refresh();
            }
        }
        
        // 查询余额按钮点击事件
        private async void CheckBalances_Click(object sender, RoutedEventArgs e)
        {
            if (_targetWallets.Count == 0)
            {
                MessageBox.Show("没有可查询的钱包", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            btnCheckBalances.IsEnabled = false;
            btnCheckBalances.Content = "查询中...";
            
            try
            {
                // 初始化钱包余额列表
                InitializeWalletBalanceList();
                
                // 执行查询
                await FetchWalletBalances();
                
                MessageBox.Show("余额查询完成", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询余额时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnCheckBalances.IsEnabled = true;
                btnCheckBalances.Content = "查询余额";
            }
        }
        
        // 获取所有钱包余额
        private async Task FetchWalletBalances()
        {
            // 获取当前项目名称
            string projectName = "";
            ComboBoxItem selectedProjectItem = cmbProject.SelectedItem as ComboBoxItem;
            projectName = selectedProjectItem?.Content?.ToString() ?? string.Empty;
            
            // 获取交互类型（这里可以根据需要添加交互类型下拉框）
            string interactionType = "all"; // 默认查询所有类型
            
            AppendLog($"开始查询 {projectName} 项目的钱包余额...");
            
            // 依次查询每个钱包余额
            for (int i = 0; i < _targetWallets.Count; i++)
            {
                try
                {
                    var wallet = _targetWallets[i];
                    
                    // 更新操作状态
                    UpdateCurrentOperation($"正在查询 {wallet.Address} 的余额... ({i+1}/{_targetWallets.Count})");
                    UpdateProgress(i, _targetWallets.Count);
                    
                    // 查询该钱包的代币余额
                    var tokenBalance = await QueryTokenBalance(wallet.Address, projectName, interactionType);
                    
                    // 更新UI显示
                    var item = _walletBalanceItems.FirstOrDefault(w => w.Address == wallet.Address);
                    if (item != null)
                    {
                        // 更新余额和颜色
                        item.Balance = $"{tokenBalance:F4}";
                        item.BalanceColor = "#4CAF50"; // 绿色
                        item.Status = "查询完成";
                        // 强制刷新UI
                        walletBalanceList.Items.Refresh();
                    }
                    
                    // 添加延迟避免API限流
                    await Task.Delay(300);
                }
                catch (Exception ex)
                {
                    // 处理查询失败
                    var wallet = _targetWallets[i];
                    var item = _walletBalanceItems.FirstOrDefault(w => w.Address == wallet.Address);
                    if (item != null)
                    {
                        item.Balance = "查询失败";
                        item.BalanceColor = "#F44336"; // 红色
                        walletBalanceList.Items.Refresh();
                    }
                    AppendLog($"查询钱包 {wallet.Address} 余额失败: {ex.Message}");
                }
            }
            
            // 更新进度为100%
            UpdateProgress(_targetWallets.Count, _targetWallets.Count);
           // UpdateCurrentOperation("余额查询完成");
        }
        
        // 根据代币类型返回对应的颜色
        private string GetColorForToken(string tokenSymbol)
        {
            switch (tokenSymbol)
            {
                case "sMON": return "#4CAF50"; // 绿色
                case "gMON": return "#2196F3"; // 蓝色
                case "LP": return "#FF9800";   // 橙色
                default: return "#000000";     // 黑色
            }
        }
        
        // 查询代币余额的统一入口
        // 返回值: (代币符号, 代币余额, 交互次数)
        private async Task<decimal> QueryTokenBalance(string walletAddress, string projectName, string interactionType)
        {
            
            // 处理不同项目
            if (projectName == "Monad")
            {
                var Balances = await GetEVMBalance.GetEthBalanceAsync(rpcUrl,walletAddress);
                return Balances;
            } 
            else
            {
                // 其他项目返回默认代币
                return (0m);
            }
        }
 
    }
    
   
}  
 