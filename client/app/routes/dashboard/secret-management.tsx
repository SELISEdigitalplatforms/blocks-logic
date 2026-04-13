import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

export default function SecretManagementPage() {
	return (
		<main className="flex flex-col gap-6 p-6">
			<div>
				<h1 className="text-xl font-semibold md:text-2xl">Secret Management</h1>
				<p className="text-muted-foreground">Manage and rotate your secrets and credentials</p>
			</div>

			<Card>
				<CardHeader>
					<CardTitle>Secret Management</CardTitle>
					<CardDescription>Securely manage your secrets and API keys</CardDescription>
				</CardHeader>
				<CardContent className="flex h-40 items-center justify-center text-muted-foreground">
					Secret Management content coming soon...
				</CardContent>
			</Card>
		</main>
	);
}
