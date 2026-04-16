import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

export default function LmtPage() {
	return (
		<main className="flex flex-col gap-6 p-6">
			<div>
				<h1 className="text-xl font-semibold md:text-2xl">LMT</h1>
				<p className="text-muted-foreground">Manage your LMT configurations and settings</p>
			</div>

			<Card>
				<CardHeader>
					{/* <CardTitle>LMT</CardTitle>
					<CardDescription>Configure your LMT preferences</CardDescription> */}
				</CardHeader>
				<CardContent className="flex h-40 items-center justify-center text-muted-foreground">
					LMT content coming soon...
				</CardContent>
			</Card>
		</main>
	);
}
