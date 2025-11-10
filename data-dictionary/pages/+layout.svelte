<script>
	import { EvidenceDefaultLayout } from "@evidence-dev/core-components";
	import { addBasePath } from "@evidence-dev/sdk/utils/svelte";
	import "@evidence-dev/tailwind/fonts.css";
	import "../app.css";
	// Banner script for the 'Here's how you know' functionality
	import { onMount } from "svelte";
	// Export the data prop which is required by EvidenceDefaultLayout
	export let data;
	// Hide the Evidence header and show only the US banner
	data = {
		...data,
		hideHeader: true,
		builtWithEvidence: false,
		title: "Wintap Data",
	};
	onMount(() => {
		// Initialize the banner toggle
		const button = document.querySelector(".usa-banner__button");
		if (button) {
			button.onclick = function () {
				const content = document.getElementById(
					this.getAttribute("aria-controls"),
				);
				const isHidden = content.hasAttribute("hidden");
				content.hidden = !isHidden;
				this.setAttribute("aria-expanded", isHidden);
			};
		}
	});
</script>

<svelte:head>
	<meta
		name="description"
		content="Explore Wintap and ACME Datasets from Lawrence Livermore National Laboratory for cybersecurity research"
	/>
	<link
		rel="stylesheet"
		media="all"
		href={addBasePath("/.assets/css/fedbanner.css")}
	/>
</svelte:head>

<!-- Government Banner instead of Evidence header -->
<div
	class="usa-banner__header"
	style="position: fixed; top: 0; left: 0; width: 100%; z-index: 1001; background-color: #f0f0f0; box-shadow: 0 2px 4px rgba(0,0,0,0.1);"
>
	<img
		src={addBasePath("/.assets/images/us_flag_small.png")}
		alt="U.S. flag"
		class="usa-banner__flag"
	/>
	<div class="usa-banner__text-group">
		<p class="usa-banner__text">
			An official website of the United States government.
		</p>
		<button
			class="usa-banner__button"
			aria-expanded="false"
			aria-controls="gov-banner-content">Here's how you know</button
		>
		<div class="usa-banner__content" id="gov-banner-content" hidden>
			<div class="usa-banner__details">
				<p>
					<strong>Official websites use .gov</strong><br />A
					<strong>.gov</strong> website belongs to an official government
					organization in the United States.
				</p>
				<p>
					<strong>Secure .gov websites use HTTPS</strong><br />A
					<strong>lock</strong>
					(🔒) or <strong>https://</strong> means you've safely connected
					to the .gov website. Share sensitive information only on official,
					secure websites.
				</p>
			</div>
		</div>
	</div>
</div>

<EvidenceDefaultLayout
	{data}
	builtWithEvidence={false}
	logo={addBasePath("/Wintap-logo.png")}
>
	<slot slot="content" />
</EvidenceDefaultLayout>

<div id="footer-text-info">
	<script
		src="https://dap.digitalgov.gov/Universal-Federated-Analytics-Min.js?agency=DOE&amp;subagency=LLNL&amp;sdor=fda.gov&amp;dclink=true"
		id="_fed_an_ua_tag"
	></script>
	<a
		href="https://www.llnl.gov/disclaimer"
		class="external-link"
		rel="nofollow">Privacy and Legal Notice</a
	><br />
	Operated by the Lawrence Livermore National Security, LLC for the Department
	of Energy's National Nuclear Security Administration<br />
	Learn about the Department of Energy's
	<a
		href="https://www.energy.gov/vulnerability-disclosure-policy"
		class="external-link"
		target="_blank"
		rel="noopener noreferrer">Vulnerability Disclosure Programs</a
	>
</div>

<style>
	/* Footer styles */
	#footer-text-info {
		padding: 2rem 1rem;
		font-size: 0.85rem;
		line-height: 1.4;
		color: #333;
		border-top: 1px solid #ddd;
		margin-top: 2rem;
	}

	#footer-text-info a {
		color: #005ea2;
		text-decoration: none;
	}

	#footer-text-info a:hover {
		text-decoration: underline;
	}
</style>
