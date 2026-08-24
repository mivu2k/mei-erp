using MeiErp.Modules.Finance;
using MeiErp.Modules.Auto;
using MeiErp.Modules.GatePass;
using MeiErp.Modules.Inventory;
using MeiErp.Modules.Trade;
using MeiErp.Modules.Repair;
using MeiErp.Modules.Hr;
using MeiErp.Modules.Ledger;
using MeiErp.Modules.Tender;
using MeiErp.Platform.Identity;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Printing;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Host.Services;

public static class RecordPrintEndpoints
{
    public static void MapRecordPrintEndpoints(this WebApplication app)
    {
        app.MapGet("/repair/photo/{id:int}",async(int id,IRepairPhotoService photos,ICurrentUser user,CancellationToken ct)=>
        {
            if(!user.Can(RepairModule.JobsView))return Results.Forbid();var file=await photos.GetAsync(id,ct);return file is null?Results.NotFound():Results.File(file.Content,file.Photo.ContentType,file.Photo.OriginalName);
        }).RequireAuthorization();
        app.MapGet("/hr/document/{id:int}",async(int id,IEmployeeDocumentService documents,ICurrentUser user,CancellationToken ct)=>
        {
            if(!user.Can(HrModule.DocumentsView))return Results.Forbid();var file=await documents.FileAsync(id,ct);
            return file?.Content is null?Results.NotFound():Results.File(file.Content,file.ContentType??"application/octet-stream",file.FileName??file.Title);
        }).RequireAuthorization();
        var group = app.MapGroup("/print").RequireAuthorization();
        group.MapGet("/gatepass/{id:int}", async (int id, string? size, IGatePassService service,
            IPrintService print, ICompanyProfileService company, ICurrentUser user, CancellationToken ct) =>
        {
            if (!user.Can(GatePassModule.PassesView)) return Results.Forbid();
            var pass = await service.GetAsync(id, ct); if (pass is null) return Results.NotFound();
            var doc = new PrintDocument { Title=$"{pass.Direction} GATE PASS", Reference=pass.Number, Date=pass.Date, Size=Size(size),
                Fields=[new("Party",pass.PartyName),new("Company",pass.CompanyName),new("Phone",pass.PersonPhone),new("CNIC / ID",pass.PersonCnic),new("Department",pass.Department),new("Vehicle",pass.VehicleNumber),new("Driver",pass.DriverName),new("Purpose",pass.Purpose),new("Reference",string.Join(" — ",new[]{pass.ReferenceType,pass.ReferenceNumber}.Where(x=>!string.IsNullOrWhiteSpace(x)))),new("Returnable",pass.IsReturnable?"Yes":"No"),new("Expected back",pass.ExpectedBack?.ToString("d MMM yyyy"))],
                Tables=[Table([("Description",4f,false),("Qty",1f,true),("Unit",1f,false),("Serial",2f,false)],pass.Items.Select(i=>(IReadOnlyList<string>)[i.Description,i.Quantity.ToString("0.##"),i.Unit,i.SerialNumber??""]))],
                Notes=pass.Notes,
                Signatures=["Raised by","Gate security","Receiver"]};
            return await Pdf(print,company,doc,pass.Number,ct);
        });
        group.MapGet("/gatepass/demo/{id:int}", async (int id, string? size, IDemoIssuanceService service,
            IPrintService print, ICompanyProfileService company, ICurrentUser user, CancellationToken ct) =>
        {
            if (!user.Can(GatePassModule.DemosView)) return Results.Forbid();
            var demo=await service.GetAsync(id,ct);if(demo is null)return Results.NotFound();
            var doc=new PrintDocument{Title="DEMO GOODS ISSUANCE",Reference=demo.Number,Date=DateOnly.FromDateTime(demo.IssuedUtc),Size=Size(size),
                Watermark=demo.Status==DemoStatus.Cancelled?"CANCELLED":demo.Status==DemoStatus.Returned?"RETURNED":null,
                Fields=[new("Customer",demo.CustomerName),new("Phone",demo.CustomerPhone),new("Department",demo.Department),new("Customer reference",demo.CustomerReference),new("Reference letter",demo.ReferenceLetter),new("Expected return",demo.ExpectedReturnOn?.ToString("d MMM yyyy")),new("Issued by",demo.IssuedByName),new("Status",demo.Status.ToString())],
                Tables=[Table([("Description",3f,false),("Serial",1.5f,false),("Qty",.8f,true),("Accessories",2f,false),("Returned",1.2f,false)],demo.Items.Select(x=>(IReadOnlyList<string>)[x.Description,x.SerialNumber??"",x.Quantity.ToString("0.##"),x.Accessories??"",x.ReturnedUtc?.ToLocalTime().ToString("d MMM yyyy")??"Outstanding"]))],
                Notes=demo.Notes,Signatures=["Issued by","Customer receiver","Returned to"]};
            return await Pdf(print,company,doc,demo.Number,ct);
        });
        group.MapGet("/repair/job/{id:int}", async (int id,string? kind,int? template,IRepairService service,IPrintService print,ICompanyProfileService company,ILabelTemplateService templates,ICurrentUser user,CancellationToken ct)=>
        {
            if(!user.Can(RepairModule.JobsView))return Results.Forbid();var job=await service.GetAsync(id,ct);if(job is null)return Results.NotFound();
            var label=string.Equals(kind,"label",StringComparison.OrdinalIgnoreCase);var delivery=string.Equals(kind,"delivery",StringComparison.OrdinalIgnoreCase);
            if(label)return await Labels(print,company,templates,[DeviceLabel(job)],$"{job.Number}-label",template,ct);
            var doc=new PrintDocument{Title=label?"DEVICE LABEL":delivery?"DELIVERY NOTE":"REPAIR JOB CARD",Reference=job.Number,Date=job.ReceivedOn,Size=label?PageSize.Label62:Size(null),Watermark=job.Status==JobStatus.Cancelled?"CANCELLED":null,
                Fields=[new("Customer",job.CustomerName),new("Device",string.Join(" ",new[]{job.Make,job.Model,job.DeviceType}.Where(s=>!string.IsNullOrWhiteSpace(s)))),new("Serial",job.SerialNumber),new("Fault",job.ReportedFault),new("Diagnosis",job.Diagnosis),new("Status",job.Status.ToString()),new("Collected by",job.CollectedBy)],
                Tables=label?[]:[Table([("Work / part",4f,false),("Qty",1f,true),("Rate",1.5f,true),("Amount",1.5f,true)],job.WorkItems.Select(i=>(IReadOnlyList<string>)[i.Description,i.Quantity.ToString("0.##"),i.UnitPrice.ToString("N2"),i.LineTotal.ToString("N2")]))],
                Totals=label?[]:[new("Billable total",job.Total.ToString("N2"))],Signatures=label?[]:delivery?["Delivered by","Collected by"]:["Technician","Customer approval"]};
            return await Pdf(print,company,doc,$"{job.Number}-{(kind??"job-card")}",ct);
        });
        group.MapGet("/repair/intake/{id:int}",async(int id,string? size,string? kind,int? template,IRepairIntakeService service,IPrintService print,ICompanyProfileService company,ILabelTemplateService templates,ICurrentUser user,CancellationToken ct)=>
        {
            if(!user.Can(RepairModule.IntakesManage))return Results.Forbid();var intake=await service.GetAsync(id,ct);if(intake is null)return Results.NotFound();var labels=string.Equals(kind,"labels",StringComparison.OrdinalIgnoreCase);
            if(labels)return await Labels(print,company,templates,intake.Jobs.Select(DeviceLabel).ToList(),$"{intake.Number}-labels",template,ct);
            (string Header,float Width,bool Right)[] columns=labels
                ? [("Job / device",3f,false),("Serial",2f,false)]
                : [("Job",1.3f,false),("Device",2.5f,false),("Serial",1.5f,false),("Condition",1.2f,false),("Fault",3f,false)];
            var rows=intake.Jobs.Select(j=>(IReadOnlyList<string>)new List<string>{j.Number,string.Join(" ",new[]{j.Make,j.Model,j.DeviceType}.Where(x=>!string.IsNullOrWhiteSpace(x))),j.SerialNumber??"",j.Condition.ToString(),j.ReportedFault}.Take(labels?2:5).ToList());
            var doc=new PrintDocument{Title=labels?"DEVICE LABELS":"DEVICE INTAKE RECEIPT",Reference=intake.Number,Date=DateOnly.FromDateTime(intake.ReceivedUtc),Size=labels?PageSize.Label62:Size(size),Fields=labels?[]:[new("Customer",intake.CustomerName),new("Received",intake.ReceivedUtc.ToLocalTime().ToString("g")),new("Received by",intake.ReceivedByName)],Tables=[Table(columns,rows)],Notes=intake.Notes,Signatures=labels?[]:["Received by","Customer"]};return await Pdf(print,company,doc,$"{intake.Number}-{(labels?"labels":size??"a4")}",ct);
        });
        group.MapGet("/purchase/part-purchase/{id:int}",async(int id,IPartProcurementService service,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {
            if(!user.Can(PurchaseModule.OrdersView))return Results.Forbid();var p=await service.PurchaseAsync(id,ct);if(p is null)return Results.NotFound();var doc=new PrintDocument{Title="PARTS PURCHASE RECEIPT",Reference=p.Number,Date=p.PurchasedOn,Fields=[new("Supplier",p.PartyName),new("Supplier invoice",p.SupplierInvoiceNumber),new("Received by",p.ReceivedByName)],Tables=[Table([("Part",4f,false),("Qty",1f,true),("Unit cost",1.5f,true),("Amount",1.5f,true)],p.Lines.Select(x=>(IReadOnlyList<string>)[x.Part?.Name??"",x.Quantity.ToString("0.##"),x.UnitCost.ToString("N2"),x.LineTotal.ToString("N2")]))],Totals=[new("Subtotal",p.Subtotal.ToString("N2")),new("Discount",p.DiscountAmount.ToString("N2")),new("Tax",p.TaxAmount.ToString("N2")),new("Other charges",p.OtherCharges.ToString("N2")),new("Total",p.Total.ToString("N2"))],Notes=p.Notes,Signatures=["Received by","Checked by","Supplier"]};return await Pdf(print,company,doc,p.Number,ct);
        });
        group.MapGet("/purchase/order/{id:int}",async(int id,TradeDbContext db,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {
            if(!user.Can(PurchaseModule.OrdersView))return Results.Forbid();var o=await db.PurchaseOrders.AsNoTracking().Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct);if(o is null)return Results.NotFound();
            var doc=new PrintDocument{Title="PURCHASE ORDER",Reference=o.Number,Date=o.Date,Watermark=o.Status==MeiErp.Modules.Trade.PurchaseOrderStatus.Draft?"DRAFT":null,Fields=[new("Supplier",o.PartyName),new("Status",o.Status.ToString())],Tables=[Table([("Item",4f,false),("Qty",1f,true),("Rate",1.5f,true),("Amount",1.5f,true)],o.Lines.Select(l=>(IReadOnlyList<string>)[l.ItemName,l.Quantity.ToString("0.##"),l.UnitCost.ToString("N2"),l.LineTotal.ToString("N2")]))],Totals=[new("Total",o.Total.ToString("N2"))],Notes=o.Notes,Signatures=["Prepared by","Approved by","Supplier"]};return await Pdf(print,company,doc,o.Number,ct);
        });
        group.MapGet("/purchase/goods-receipt/{id:int}",async(int id,TradeDbContext db,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {
            if(!user.Can(PurchaseModule.OrdersView))return Results.Forbid();var r=await db.GoodsReceipts.AsNoTracking().Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct);if(r is null)return Results.NotFound();
            var doc=new PrintDocument{Title="GOODS RECEIVED NOTE",Reference=r.Number,Date=r.Date,Fields=[new("Supplier",r.PartyName),new("Purchase order",r.PurchaseOrderId.ToString())],Tables=[Table([("Item",4f,false),("Qty",1f,true),("Cost",1.5f,true),("Value",1.5f,true)],r.Lines.Select(l=>(IReadOnlyList<string>)[l.ItemName,l.Quantity.ToString("0.##"),l.UnitCost.ToString("N2"),(l.Quantity*l.UnitCost).ToString("N2")]))],Totals=[new("Total value",r.Total.ToString("N2"))],Notes=r.Notes,Signatures=["Received by","Checked by","Supplier"]};return await Pdf(print,company,doc,r.Number,ct);
        });
        group.MapGet("/inventory/return/{id:int}",async(int id,IInventoryReturnService service,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {if(!user.Can(InventoryModule.ReturnsPost))return Results.Forbid();var r=await service.GetAsync(id,ct);if(r is null)return Results.NotFound();var doc=new PrintDocument{Title=r.Kind==InventoryReturnKind.SalesReturn?"CUSTOMER RETURN NOTE":"SUPPLIER RETURN NOTE",Reference=r.Number,Date=r.Date,Fields=[new("Party",r.PartyName),new("Original document",r.SourceReference),new("Reason",r.Reason),new("Posted by",r.PostedByName)],Tables=[Table([("Item",3f,false),("Qty",1f,true),("Unit cost",1.3f,true),("Serials / batch",2.5f,false)],r.Lines.Select(x=>(IReadOnlyList<string>)[x.ItemCode+" — "+x.ItemName,x.Quantity.ToString("0.##"),x.UnitCost.ToString("N2"),string.Join(" · ",new[]{x.SerialNumbers,x.BatchNumber}.Where(y=>!string.IsNullOrWhiteSpace(y)))]))],Totals=[new("Value",r.Total.ToString("N2"))],Notes=r.Notes,Signatures=["Posted by",r.Kind==InventoryReturnKind.SalesReturn?"Customer":"Supplier","Storekeeper"]};return await Pdf(print,company,doc,r.Number,ct);});
        group.MapGet("/finance/voucher/{id:int}",async(int id,IVoucherService service,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {
            if(!user.Can(FinanceModule.VouchersView))return Results.Forbid();var v=await service.GetAsync(id,ct);if(v is null)return Results.NotFound();
            var doc=new PrintDocument{Title=$"{v.Type.ToString().ToUpperInvariant()} VOUCHER",Reference=v.Number,Date=v.Date,Watermark=v.Status==VoucherStatus.Draft?"DRAFT":v.Status==VoucherStatus.Reversed?"REVERSED":null,Fields=[new("Status",v.Status.ToString()),new("Source",v.SourceReference)],Tables=[Table([("Account",3f,false),("Narration",3f,false),("Debit",1.5f,true),("Credit",1.5f,true)],v.Lines.Select(l=>(IReadOnlyList<string>)[l.AccountCode+" — "+l.AccountName,l.Narration??"",l.Debit==0?"":l.Debit.ToString("N2"),l.Credit==0?"":l.Credit.ToString("N2")]))],Totals=[new("Debit",v.TotalDebit.ToString("N2")),new("Credit",v.TotalCredit.ToString("N2"))],Notes=v.Narration,Signatures=["Prepared by","Checked by","Approved by"]};return await Pdf(print,company,doc,v.Number,ct);
        });
        group.MapGet("/finance/payslip/{id:int}",async(int id,FinanceDbContext db,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {
            var p=await db.Payslips.AsNoTracking().Include(x=>x.Lines).Include(x=>x.Run).FirstOrDefaultAsync(x=>x.Id==id,ct);if(p is null)return Results.NotFound();if(!user.Can(FinanceModule.PayrollView)&&p.UserId!=user.UserId)return Results.Forbid();
            var doc=new PrintDocument{Title="PAYSLIP",Reference=$"PAY-{p.RunId}-{p.EmployeeCode}",Date=p.Run!.Month,Fields=[new("Employee",p.EmployeeCode+" — "+p.EmployeeName),new("Pay month",p.Run.Month.ToString("MMMM yyyy")),new("Attendance",$"{p.DaysWorked:0.##} / {p.DaysInMonth:0.##} days")],Tables=[Table([("Component",4f,false),("Kind",1.5f,false),("Amount",2f,true)],p.Lines.Select(l=>(IReadOnlyList<string>)[l.Name,l.Kind.ToString(),l.Amount.ToString("N2")]))],Totals=[new("Gross",p.Gross.ToString("N2")),new("Deductions",p.TotalDeductions.ToString("N2")),new("Net pay",p.Net.ToString("N2"))],Signatures=["Employee","Authorized by"]};return await Pdf(print,company,doc,$"Payslip-{p.EmployeeCode}",ct);
        });
        group.MapGet("/sales/order/{id:int}",async(int id,MeiErp.Modules.Trade.ISalesService service,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {if(!user.Can(SalesModule.OrdersView))return Results.Forbid();var o=await service.GetOrderAsync(id,ct);if(o is null)return Results.NotFound();var doc=new PrintDocument{Title="SALES ORDER",Reference=o.Number,Date=o.Date,Watermark=o.Status==SalesOrderStatus.Draft?"DRAFT":null,Fields=[new("Customer",o.PartyName),new("Status",o.Status.ToString())],Tables=[Table([("Item",4f,false),("Qty",1f,true),("Rate",1.5f,true),("Amount",1.5f,true)],o.Lines.Select(l=>(IReadOnlyList<string>)[l.ItemName,l.Quantity.ToString("0.##"),l.UnitPrice.ToString("N2"),l.LineTotal.ToString("N2")]))],Totals=[new("Total",o.Total.ToString("N2"))],Notes=o.Notes,Signatures=["Prepared by","Customer"]};return await Pdf(print,company,doc,o.Number,ct);});
        group.MapGet("/sales/delivery/{id:int}",async(int id,TradeDbContext db,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {if(!user.Can(SalesModule.OrdersView))return Results.Forbid();var d=await db.Deliveries.AsNoTracking().Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct);if(d is null)return Results.NotFound();var doc=new PrintDocument{Title="DELIVERY NOTE",Reference=d.Number,Date=d.Date,Fields=[new("Customer",d.PartyName),new("Collected by",d.CollectedBy),new("Sales order",d.SalesOrderId.ToString())],Tables=[Table([("Item",4f,false),("Quantity",1.5f,true)],d.Lines.Select(l=>(IReadOnlyList<string>)[l.ItemName,l.Quantity.ToString("0.##")]))],Notes=d.Notes,Signatures=["Delivered by","Collected by"]};return await Pdf(print,company,doc,d.Number,ct);});
        group.MapGet("/tender/{id:int}",async(int id,ITenderService service,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {if(!user.Can(TenderModule.TendersView))return Results.Forbid();var t=await service.GetTenderAsync(id,ct);if(t is null)return Results.NotFound();var doc=new PrintDocument{Title="TENDER SUMMARY",Reference=t.Reference,Date=t.PublishedOn,Watermark=t.Status==TenderStatus.Cancelled?"CANCELLED":null,Fields=[new("Title",t.Title),new("Client",t.ClientName),new("Status",t.Status.ToString()),new("Submission deadline",t.SubmissionDeadline?.ToString("d MMM yyyy")),new("Opening date",t.OpeningDate?.ToString("d MMM yyyy")),new("Owner",t.OwnerName),new("Estimated value",t.EstimatedValue?.ToString("N2"))],Tables=[Table([("Description",4f,false),("Qty",1f,true),("Unit",1f,false),("Rate",1.5f,true),("Amount",1.5f,true)],t.Items.Select(i=>(IReadOnlyList<string>)[i.Description,i.Quantity.ToString("0.##"),i.Unit,i.UnitRate.ToString("N2"),i.LineTotal.ToString("N2")]))],Totals=[new("Bid total",t.ItemsTotal.ToString("N2"))],Notes=t.Notes,Signatures=["Prepared by","Reviewed by","Authorized by"]};return await Pdf(print,company,doc,t.Reference,ct);});
        group.MapGet("/tender/file-sticker/{id:int}",async(int id,IFileRegistryService files,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {if(!user.Can(TenderModule.FilesView))return Results.Forbid();var f=await files.GetAsync(id,ct);if(f is null)return Results.NotFound();var doc=new PrintDocument{Title="PHYSICAL FILE",Reference=f.FileNumber,Date=f.OpenedOn,Size=PageSize.Label62,Fields=[new("Owner",f.OwnerReference),new("Title",f.OwnerTitle),new("Volume",f.VolumeNumber),new("Location",f.Location)]};return await Pdf(print,company,doc,$"{f.FileNumber}-sticker",ct);});
        group.MapGet("/tender/file-movements/{id:int}",async(int id,IFileRegistryService files,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {if(!user.Can(TenderModule.FilesView))return Results.Forbid();var f=await files.GetAsync(id,ct);if(f is null)return Results.NotFound();var doc=new PrintDocument{Title="FILE MOVEMENT REGISTER",Reference=f.FileNumber,Date=f.OpenedOn,Fields=[new("Owner",f.OwnerReference+" — "+f.OwnerTitle),new("Status",f.Status.ToString()),new("Current holder",f.HolderName),new("Location",f.Location),new("Volume",f.VolumeNumber)],Tables=[Table([("Date",1.2f,false),("Action",1.2f,false),("From",2f,false),("To",2f,false),("Purpose / remarks",3f,false),("Due",1.2f,false)],f.Movements.OrderBy(x=>x.MovedOn).ThenBy(x=>x.Id).Select(x=>(IReadOnlyList<string>)[x.MovedOn.ToString("d MMM yyyy"),x.Action.ToString(),x.FromHolderName??x.FromLocation??"",x.ToHolderName??x.ToLocation??"",string.Join(" · ",new[]{x.Purpose,x.Remarks}.Where(v=>!string.IsNullOrWhiteSpace(v))),x.DueBack?.ToString("d MMM yyyy")??""]))],Signatures=["Registry clerk","Verified by"]};return await Pdf(print,company,doc,$"{f.FileNumber}-movements",ct);});
        group.MapGet("/auto/vehicle/{id:int}",async(int id,IFleetService service,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {if(!user.Can(AutoModule.VehiclesView))return Results.Forbid();var v=await service.GetAsync(id,ct);if(v is null)return Results.NotFound();var rows=await service.ServicesAsync(id,ct);var doc=new PrintDocument{Title="VEHICLE HISTORY",Reference=v.Registration,Date=v.PurchasedOn,Fields=[new("Vehicle",string.Join(" ",new[]{v.Make,v.Model,v.Year?.ToString()}.Where(x=>!string.IsNullOrWhiteSpace(x)))),new("Chassis",v.ChassisNumber),new("Engine",v.EngineNumber),new("Assigned to",v.AssignedTo),new("Odometer",v.CurrentOdometer?.ToString("N0")),new("Registration expiry",v.RegistrationExpiry?.ToString("d MMM yyyy")),new("Insurance expiry",v.InsuranceExpiry?.ToString("d MMM yyyy")),new("Status",v.Status.ToString())],Tables=[Table([("Date",1.3f,false),("Type",1.3f,false),("Description",3f,false),("Vendor",2f,false),("Cost",1.5f,true)],rows.Select(s=>(IReadOnlyList<string>)[s.Date.ToString("d MMM yyyy"),s.Kind.ToString(),s.Description,s.Vendor??"",s.Cost.ToString("N2")]))],Totals=[new("Total running cost",rows.Sum(s=>s.Cost).ToString("N2"))],Signatures=["Fleet officer","Reviewed by"]};return await Pdf(print,company,doc,v.Registration,ct);});
        group.MapGet("/hr/employee/{id:int}",async(int id,HrDbContext db,IPrintService print,ICompanyProfileService company,ICurrentUser user,CancellationToken ct)=>
        {if(!user.Can(HrModule.EmployeesView))return Results.Forbid();var e=await db.Employees.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id,ct);if(e is null)return Results.NotFound();var doc=new PrintDocument{Title="EMPLOYEE PROFILE",Reference=e.Code,Date=e.JoinedOn,Fields=[new("Name",e.FullName),new("Department",e.DepartmentName),new("Designation",e.Designation),new("Email",e.Email),new("Phone",e.Phone),new("CNIC",e.Cnic),new("Joined",e.JoinedOn.ToString("d MMM yyyy")),new("Status",e.Status.ToString())],Signatures=["Employee","HR","Authorized by"]};return await Pdf(print,company,doc,e.Code,ct);});
        group.MapGet("/ledger/{id:int}",async(int id,ILedgerService service,IPrintService print,ICompanyProfileService company,ICurrentUser user,IClock clock,CancellationToken ct)=>
        {if(!user.Can(LedgerPermissions.View))return Results.Forbid();var l=await service.GetAsync(id,ct);if(l is null)return Results.NotFound();var lines=await service.GetStatementAsync(id,ct:ct);var balance=await service.GetBalanceAsync(id,ct);var doc=new PrintDocument{Title="LEDGER STATEMENT",Reference=$"LEDGER-{l.Id}",Date=clock.Today,Fields=[new("Ledger",l.Name),new("Counterparty",l.CounterpartyName),new("Nature",l.Nature.ToString()),new("Status",l.Status.ToString())],Tables=[Table([("Date",1.3f,false),("Description",3.5f,false),("In",1.3f,true),("Out",1.3f,true),("Balance",1.5f,true)],lines.Select(x=>(IReadOnlyList<string>)[x.Entry.Date.ToString("d MMM yyyy"),x.Entry.Description,x.Entry.Direction==LedgerDirection.In?x.Entry.Amount.ToString("N2"):"",x.Entry.Direction==LedgerDirection.Out?x.Entry.Amount.ToString("N2"):"",x.RunningBalance.ToString("N2")]))],Totals=[new("Own balance",balance.Own.ToString("N2")),new("Tree roll-up",balance.Rollup.ToString("N2"))],Notes=l.Notes,Signatures=["Prepared by","Counterparty"]};return await Pdf(print,company,doc,$"Ledger-{l.Id}",ct);});
    }

    private static PageSize Size(string? value)=>string.Equals(value,"roll",StringComparison.OrdinalIgnoreCase)?PageSize.Roll80:PageSize.A4;
    private static PrintTable Table((string Header,float Width,bool Right)[] columns,IEnumerable<IReadOnlyList<string>> rows)=>new(){Columns=columns.Select(c=>new PrintColumn(c.Header,c.Width,c.Right)).ToList(),Rows=rows.ToList()};
    private static async Task<IResult> Pdf(IPrintService print,ICompanyProfileService company,PrintDocument document,string name,CancellationToken ct)
    {var branding=ReportEndpoints.ToBranding(await company.GetAsync(ct));return Results.File(print.ToPdf(document,branding),"application/pdf",$"{name}.pdf");}
    private static LabelData DeviceLabel(Job job)=>new(string.Join(" ",new[]{job.Make,job.Model,job.DeviceType}.Where(x=>!string.IsNullOrWhiteSpace(x))),job.Number,new Dictionary<string,string?>{{"customer",job.CustomerName},{"device",job.DeviceType},{"serial",job.SerialNumber},{"fault",job.ReportedFault},{"status",job.Status.ToString()},{"intake",job.Intake?.Number}});
    private static async Task<IResult> Labels(IPrintService print,ICompanyProfileService company,ILabelTemplateService templates,IReadOnlyList<LabelData> data,string name,int? templateId,CancellationToken ct)
    {
        var saved=templateId is { } id?await templates.GetAsync(id,ct):await templates.GetDefaultAsync(LabelDocumentTypes.RepairDevice,ct);
        if(saved is not null&&saved.DocumentType!=LabelDocumentTypes.RepairDevice)saved=null;
        var layout=saved is null?LabelLayout.Fallback(["customer","device","serial"]):new LabelLayout(saved.WidthMm,saved.HeightMm,saved.MarginMm,saved.SelectedFields(),saved.ShowTitle,saved.ShowCompanyName,saved.ShowBarcode,saved.ShowQrCode,saved.FontScale);
        var branding=ReportEndpoints.ToBranding(await company.GetAsync(ct));return Results.File(print.ToLabels(data,layout,branding),"application/pdf",$"{name}.pdf");
    }
}
