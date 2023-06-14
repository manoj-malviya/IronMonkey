<script>
    import { Navbar, NavBrand, NavLi, NavUl, NavHamburger, Dropdown, DropdownItem, Chevron, DropdownDivider, Input, DropdownHeader, Avatar } from 'flowbite-svelte';
    import { page } from '$app/stores'
  </script>
  
  <Navbar class="fixed z-30 w-full bg-gray-50" navClass="fixed z-30 w-full bg-gray-50" navDivClass="py-3 px-3 lg:px-5 lg:pl-3">
    <div class="flex justify-between items-center">
        <div class="flex justify-start items-center">
            <NavBrand href="/" class="text-md font-semibold flex items-center lg:mr-1.5">
            <img
                src="/images/logo.svg"
                class="mr-3 h-6 sm:h-9"
                alt="Flowbite Logo"
            />
            <span class="self-center whitespace-nowrap text-xl font-semibold dark:text-white">
                Flowbite
            </span>
            </NavBrand>
        </div>
        {#if $page.data.user}
        <div class="flex items-center md:order-2">
            <div class="hidden relative md:block">
                <div class="flex absolute inset-y-0 left-0 items-center pl-3 pointer-events-none">
                  <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-6 h-6 dark:text-white"><path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" /></svg>
                </div>
                <Input id="search-navbar" class="pl-10" placeholder="Search..." />
              </div>
            <Avatar id="avatar-menu" class="ml-2" src="/images/profile-picture-3.webp" />
        </div>
        <Dropdown placement="bottom" triggeredBy="#avatar-menu">
            <DropdownHeader>
            <span class="block text-sm"> Bonnie Green </span>
            <span class="block truncate text-sm font-medium"> name@flowbite.com </span>
            </DropdownHeader>
            <DropdownItem>Dashboard</DropdownItem>
            <DropdownItem>Settings</DropdownItem>
            <DropdownItem>Earnings</DropdownItem>
            <DropdownDivider />
            <DropdownItem>Sign out</DropdownItem>
          </Dropdown>
        {/if}
        {#if !$page.data.user}
        <NavUl divClass="flex items-center">
        <NavLi href="/" active={true}>Home</NavLi>
        <NavLi id="nav-menu1" class="cursor-pointer"><Chevron aligned>Dropdown</Chevron></NavLi>
        <NavLi href="/services">Services</NavLi>
        <NavLi href="/pricing">Pricing</NavLi>
        <NavLi href="/contact">Contact</NavLi>
        <Dropdown triggeredBy="#nav-menu1" class="w-44 z-20">
            <DropdownItem>Dashboard</DropdownItem>
            <DropdownItem>Settings</DropdownItem>
            <DropdownItem>Earnings</DropdownItem>
            <DropdownDivider />
            <DropdownItem>Sign out</DropdownItem>
        </Dropdown>
        </NavUl>
        {/if}
    </div>
  </Navbar>