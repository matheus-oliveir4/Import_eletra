import PurchaseOrderWorkspace from "../../../components/PurchaseOrderWorkspace";

export default async function PurchaseOrderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <PurchaseOrderWorkspace id={id} />;
}
