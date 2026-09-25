import "./styles.css";
import AuthGate from "../components/AuthGate";

export const metadata = {
  title: "ERP Comex | POs TOTVS",
  description: "Acompanhamento de POs TOTVS e importações"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="pt-BR">
      <body><AuthGate>{children}</AuthGate></body>
    </html>
  );
}
